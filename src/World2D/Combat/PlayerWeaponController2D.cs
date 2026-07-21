using System;
using Godot;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Noise;
using LineZero.World2D.Noise;

namespace LineZero.World2D.Combat;

public sealed partial class PlayerWeaponController2D : Node2D, INoiseEmitter2D
{
    private readonly record struct ValidatedShotPath(
        Vector2 RayStart,
        Vector2 RayEnd);

    private const string FireAction = "fire";
    private const string ReloadAction = "reload";
    private const float MinimumSegmentLengthSquared = 0.0001f;

    private readonly Godot.Collections.Array<Rid> _rayExclusions = new();

    private PlayerController2D? _player;
    private InventoryModel? _inventory;
    private HealthModel? _health;
    private FirearmState? _state;
    private NoiseSystem2D? _noiseSystem;
    private Node2D _aimPivot = null!;
    private Marker2D _weaponOrigin = null!;
    private Marker2D _muzzlePoint = null!;
    private Line2D _tracerLine = null!;
    private Timer _tracerTimer = null!;
    private Timer _reloadTimer = null!;
    private bool _isInitialized;
    private bool _isCombatInputEnabled;
    private ulong _blockFireThroughProcessFrame;
    private double _nextFireAllowedAtSeconds;
    private double _nextEmptyMessageAllowedAtSeconds;

    [Export]
    public FirearmDefinition? WeaponDefinition { get; set; }

    [Export(PropertyHint.Range, "0,999,1,or_greater")]
    public int InitialMagazineAmmo { get; set; } = 3;

    [Export(PropertyHint.Layers2DPhysics)]
    public uint ShotCollisionMask { get; set; } =
        CollisionLayers2D.World | CollisionLayers2D.DamageableTarget;

    [Export(PropertyHint.Range, "0.1,5.0,0.1,or_greater")]
    public double EmptyMessageIntervalSeconds { get; set; } = 0.5;

    [Export(PropertyHint.Range, "0.0,8.0,0.1")]
    public float MuzzleClearanceMargin { get; set; } = 1.0f;

    public FirearmState State => _state
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerWeaponController2D)} on '{Name}' has no firearm state.");

    public bool IsCombatInputEnabled => _isCombatInputEnabled;

    public event Action<FirearmShotResult>? ShotAttempted;

    public event Action<ReloadResult>? ReloadChanged;

    public event Action<string>? MessageRequested;

    public override void _Ready()
    {
        FirearmDefinition definition = WeaponDefinition
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' requires a weapon definition.");
        definition.Validate();

        if (InitialMagazineAmmo < 0 ||
            InitialMagazineAmmo > definition.MagazineCapacity)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' requires initial magazine " +
                $"ammunition between 0 and {definition.MagazineCapacity}.");
        }

        uint requiredCollisionLayers =
            CollisionLayers2D.World | CollisionLayers2D.DamageableTarget;
        if ((ShotCollisionMask & requiredCollisionLayers) != requiredCollisionLayers)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' must raycast against " +
                "World and DamageableTarget layers.");
        }

        if (EmptyMessageIntervalSeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' requires a positive " +
                "empty-message interval.");
        }

        if (!float.IsFinite(MuzzleClearanceMargin) || MuzzleClearanceMargin < 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' requires a non-negative " +
                "finite muzzle-clearance margin.");
        }

        _aimPivot = RequireNode<Node2D>("%AimPivot");
        _weaponOrigin = RequireNode<Marker2D>("%WeaponOrigin");
        _muzzlePoint = RequireNode<Marker2D>("%MuzzlePoint");
        _tracerLine = RequireNode<Line2D>("%TracerLine");
        _tracerTimer = RequireNode<Timer>("%TracerTimer");
        _reloadTimer = RequireNode<Timer>("%ReloadTimer");

        if (!_tracerTimer.OneShot || !_reloadTimer.OneShot)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' requires one-shot timers.");
        }

        _tracerTimer.Timeout += OnTracerTimerTimeout;
        _reloadTimer.Timeout += OnReloadTimerTimeout;
        _tracerLine.Visible = false;
        _state = new FirearmState(definition, InitialMagazineAmmo);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_tracerTimer))
        {
            _tracerTimer.Timeout -= OnTracerTimerTimeout;
        }

        if (GodotObject.IsInstanceValid(_reloadTimer))
        {
            _reloadTimer.Timeout -= OnReloadTimerTimeout;
        }

        if (_health is not null)
        {
            _health.Died -= OnOwnerDied;
        }

        _rayExclusions.Clear();
        _noiseSystem = null;
        _isInitialized = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(FireAction))
        {
            if (!_isCombatInputEnabled)
            {
                return;
            }

            if (Engine.GetProcessFrames() <= _blockFireThroughProcessFrame)
            {
                GetViewport().SetInputAsHandled();
                return;
            }

            TryFire();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_isCombatInputEnabled || !@event.IsActionPressed(ReloadAction))
        {
            return;
        }

        TryBeginReload();
        GetViewport().SetInputAsHandled();
    }

    public void Initialize(
        PlayerController2D player,
        InventoryModel inventory,
        HealthModel health)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(health);

        if (_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' is already initialized.");
        }

        if (!GodotObject.IsInstanceValid(player) ||
            !player.IsInsideTree() ||
            !ReferenceEquals(GetParent(), player))
        {
            throw new ArgumentException(
                "The weapon controller requires its active parent player.",
                nameof(player));
        }

        _player = player;
        _inventory = inventory;
        _health = health;
        _rayExclusions.Add(player.GetRid());
        _health.Died += OnOwnerDied;
        _isInitialized = true;
    }

    public void SetCombatInputEnabled(bool enabled)
    {
        EnsureInitialized();

        _isCombatInputEnabled = enabled && Health.IsAlive;
        if (!_isCombatInputEnabled)
        {
            CancelReload();
            return;
        }

        _blockFireThroughProcessFrame = Engine.GetProcessFrames();
    }

    public FirearmShotResult TryFire()
    {
        EnsureInitialized();

        FirearmShotResult result;
        if (!_isCombatInputEnabled)
        {
            result = FirearmShotResult.Rejected(
                FirearmShotStatus.CombatDisabled,
                State.CurrentMagazineAmmo,
                "Combat input is disabled.");
            ShotAttempted?.Invoke(result);
            return result;
        }

        if (Health.IsDead)
        {
            result = FirearmShotResult.Rejected(
                FirearmShotStatus.OwnerDead,
                State.CurrentMagazineAmmo,
                "Dead actors cannot fire.");
            ShotAttempted?.Invoke(result);
            return result;
        }

        if (!State.CanFire)
        {
            result = State.TryConsumeRound();
            PublishShotResult(result);
            return result;
        }

        double nowSeconds = Time.GetTicksMsec() / 1000.0;
        if (nowSeconds < _nextFireAllowedAtSeconds)
        {
            result = FirearmShotResult.Rejected(
                FirearmShotStatus.FireInterval,
                State.CurrentMagazineAmmo,
                "Weapon is cycling.");
            ShotAttempted?.Invoke(result);
            return result;
        }

        if (!TryResolveValidatedShotPath(out ValidatedShotPath shotPath))
        {
            result = FirearmShotResult.Rejected(
                FirearmShotStatus.MuzzleObstructed,
                State.CurrentMagazineAmmo,
                "Muzzle obstructed.");
            ShotAttempted?.Invoke(result);
            return result;
        }

        result = State.TryConsumeRound();
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "A firearm that passed prevalidation failed to consume a round.");
        }

        _nextFireAllowedAtSeconds =
            nowSeconds + State.Definition.FireIntervalSeconds;
        PerformHitscan(shotPath);
        EmitGunshotNoise(shotPath.RayStart);
        ShotAttempted?.Invoke(result);
        return result;
    }

    public ReloadResult TryBeginReload()
    {
        EnsureInitialized();

        ReloadResult result;
        if (!_isCombatInputEnabled)
        {
            result = ReloadResult.Rejected(
                ReloadStatus.CombatDisabled,
                State.CurrentMagazineAmmo,
                "Combat input is disabled.");
            ReloadChanged?.Invoke(result);
            return result;
        }

        if (Health.IsDead)
        {
            result = ReloadResult.Rejected(
                ReloadStatus.OwnerDead,
                State.CurrentMagazineAmmo,
                "Dead actors cannot reload.");
            ReloadChanged?.Invoke(result);
            return result;
        }

        string ammoItemId = GetAmmoItemId();
        int reserveAmmo = Inventory.CountByItemId(ammoItemId);
        result = State.TryBeginReload(reserveAmmo);
        if (result.Status == ReloadStatus.Started)
        {
            _reloadTimer.Start(State.Definition.ReloadDurationSeconds);
        }
        else if (result.Status == ReloadStatus.NoReserveAmmo)
        {
            MessageRequested?.Invoke(result.Message);
        }

        ReloadChanged?.Invoke(result);
        return result;
    }

    public void BindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' already has a noise system.");
        }

        if (!GodotObject.IsInstanceValid(noiseSystem) || !noiseSystem.IsInsideTree())
        {
            throw new ArgumentException("The noise system must be active.", nameof(noiseSystem));
        }

        _noiseSystem = noiseSystem;
    }

    public void UnbindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (ReferenceEquals(_noiseSystem, noiseSystem))
        {
            _noiseSystem = null;
        }
    }

    public ReloadResult CancelReload()
    {
        FirearmState state = State;
        if (GodotObject.IsInstanceValid(_reloadTimer))
        {
            _reloadTimer.Stop();
        }

        ReloadResult result = state.CancelReload();
        if (result.StateChanged)
        {
            ReloadChanged?.Invoke(result);
        }

        return result;
    }

    private PlayerController2D Player => _player
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerWeaponController2D)} on '{Name}' has no player dependency.");

    private InventoryModel Inventory => _inventory
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerWeaponController2D)} on '{Name}' has no inventory dependency.");

    private HealthModel Health => _health
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerWeaponController2D)} on '{Name}' has no health dependency.");

    private void OnReloadTimerTimeout()
    {
        if (!State.IsReloading)
        {
            return;
        }

        if (!_isCombatInputEnabled || Health.IsDead)
        {
            CancelReload();
            return;
        }

        int roundsNeeded = State.RoundsNeededToFillMagazine;
        string ammoItemId = GetAmmoItemId();
        int availableReserveAmmo = Inventory.CountByItemId(ammoItemId);
        int requestedRounds = Math.Min(roundsNeeded, availableReserveAmmo);
        int removedRounds = 0;

        if (requestedRounds > 0)
        {
            InventoryItemRemovalResult removal = Inventory.TryRemoveByItemId(
                ammoItemId,
                requestedRounds);
            removedRounds = removal.RemovedQuantity;
        }

        ReloadResult result = State.CompleteReload(removedRounds);
        if (result.LoadedRounds != removedRounds)
        {
            throw new InvalidOperationException(
                "Reload completion did not load the exact removed reserve quantity.");
        }

        ReloadChanged?.Invoke(result);
        if (removedRounds == 0)
        {
            MessageRequested?.Invoke("No reserve ammunition.");
        }
    }

    private void OnTracerTimerTimeout()
    {
        _tracerLine.Visible = false;
        _tracerLine.ClearPoints();
    }

    private void OnOwnerDied(DamageInfo damage, HealthChangeResult result)
    {
        SetCombatInputEnabled(false);
    }

    private void PerformHitscan(ValidatedShotPath shotPath)
    {
        PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(
            shotPath.RayStart,
            shotPath.RayEnd,
            ShotCollisionMask);
        query.CollideWithAreas = true;
        query.CollideWithBodies = true;
        query.HitFromInside = true;
        query.Exclude = _rayExclusions;

        Godot.Collections.Dictionary hit =
            GetWorld2D().DirectSpaceState.IntersectRay(query);
        Vector2 tracerEnd = shotPath.RayEnd;
        if (hit.Count > 0)
        {
            tracerEnd = hit["position"].AsVector2();
            GodotObject? collider = hit["collider"].AsGodotObject();
            TryApplyHitDamage(collider);
        }

        ShowTracer(shotPath.RayStart, tracerEnd);
    }

    private bool TryResolveValidatedShotPath(out ValidatedShotPath shotPath)
    {
        Vector2 safeWeaponOrigin = _weaponOrigin.GlobalPosition;
        Vector2 desiredMuzzlePosition = _muzzlePoint.GlobalPosition;
        Vector2 direction = Vector2.Right.Rotated(_aimPivot.GlobalRotation).Normalized();

        if (!IsFinite(safeWeaponOrigin) ||
            !IsFinite(desiredMuzzlePosition) ||
            !IsFinite(direction) ||
            direction.LengthSquared() <= MinimumSegmentLengthSquared)
        {
            shotPath = default;
            return false;
        }

        Vector2 originToMuzzle = desiredMuzzlePosition - safeWeaponOrigin;
        if (originToMuzzle.LengthSquared() > MinimumSegmentLengthSquared)
        {
            Vector2 clearanceEnd =
                desiredMuzzlePosition + direction * MuzzleClearanceMargin;
            PhysicsRayQueryParameters2D clearanceQuery =
                PhysicsRayQueryParameters2D.Create(
                    safeWeaponOrigin,
                    clearanceEnd,
                    CollisionLayers2D.World);
            clearanceQuery.CollideWithAreas = false;
            clearanceQuery.CollideWithBodies = true;
            clearanceQuery.HitFromInside = true;
            clearanceQuery.Exclude = _rayExclusions;

            Godot.Collections.Dictionary obstruction =
                GetWorld2D().DirectSpaceState.IntersectRay(clearanceQuery);
            if (obstruction.Count > 0)
            {
                shotPath = default;
                return false;
            }
        }

        Vector2 rayEnd = desiredMuzzlePosition + direction * State.Definition.Range;
        if (!IsFinite(rayEnd))
        {
            shotPath = default;
            return false;
        }

        // The actual physics ray starts inside the player's stable body footprint.
        // MuzzlePoint is presentation-only after its path has been proven unobstructed.
        shotPath = new ValidatedShotPath(safeWeaponOrigin, rayEnd);
        return true;
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }

    private void EmitGunshotNoise(Vector2 validatedShotOrigin)
    {
        NoiseSystem2D noiseSystem = _noiseSystem
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' has no bound noise system.");
        noiseSystem.EmitNoise(
            Player,
            NoiseKind.Gunshot,
            1.0f,
            validatedShotOrigin,
            Player,
            "Service pistol gunshot");
    }

    private void TryApplyHitDamage(GodotObject? collider)
    {
        if (collider is not Node colliderNode ||
            !GodotObject.IsInstanceValid(colliderNode))
        {
            return;
        }

        IHealthOwner? healthOwner = ResolveHealthOwner(colliderNode);
        if (healthOwner is null ||
            ReferenceEquals(healthOwner, Player) ||
            ReferenceEquals(healthOwner.Health, Health) ||
            healthOwner.Health.IsDead)
        {
            return;
        }

        DamageInfo damage = new(
            State.Definition.Damage,
            Player,
            State.Definition.DisplayName);
        healthOwner.Health.ApplyDamage(damage);
    }

    private static IHealthOwner? ResolveHealthOwner(Node colliderNode)
    {
        if (colliderNode is IHealthOwner directOwner)
        {
            return directOwner;
        }

        Node? parent = colliderNode.GetParent();
        return parent is not null &&
               GodotObject.IsInstanceValid(parent) &&
               parent is IHealthOwner parentOwner
            ? parentOwner
            : null;
    }

    private void ShowTracer(Vector2 rayStart, Vector2 rayEnd)
    {
        _tracerLine.ClearPoints();
        _tracerLine.GlobalPosition = Vector2.Zero;
        _tracerLine.GlobalRotation = 0.0f;
        _tracerLine.GlobalScale = Vector2.One;
        _tracerLine.AddPoint(rayStart);
        _tracerLine.AddPoint(rayEnd);
        _tracerLine.Visible = true;
        _tracerTimer.Stop();
        _tracerTimer.Start();
    }

    private void PublishShotResult(FirearmShotResult result)
    {
        ShotAttempted?.Invoke(result);
        if (result.Status != FirearmShotStatus.EmptyMagazine)
        {
            return;
        }

        double nowSeconds = Time.GetTicksMsec() / 1000.0;
        if (nowSeconds < _nextEmptyMessageAllowedAtSeconds)
        {
            return;
        }

        _nextEmptyMessageAllowedAtSeconds =
            nowSeconds + EmptyMessageIntervalSeconds;
        MessageRequested?.Invoke(result.Message);
    }

    private string GetAmmoItemId()
    {
        return State.Definition.AmmoItemDefinition?.Id
            ?? throw new InvalidOperationException(
                "A validated firearm definition lost its ammunition item.");
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' is not initialized.");
        }
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' requires '{path}'.");
    }
}
