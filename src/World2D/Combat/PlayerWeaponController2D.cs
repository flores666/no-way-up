using System;
using Godot;
using LineZero.Core.Events;
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
    private readonly FirearmReloadService _reloadService = new();

    private PlayerController2D? _player;
    private InventoryModel? _inventory;
    private HealthModel? _health;
    private FirearmState? _state;
    private NoiseSystem2D? _noiseSystem;
    private Node2D _aimPivot = null!;
    private Marker2D _weaponOrigin = null!;
    private Marker2D _muzzlePoint = null!;
    private Sprite2D _weaponSprite = null!;
    private Line2D _tracerLine = null!;
    private Timer _tracerTimer = null!;
    private Timer _reloadTimer = null!;
    private bool _isInitialized;
    private bool _isCombatInputEnabled;
    private ulong _blockFireThroughProcessFrame;
    private double _nextFireAllowedAtSeconds;
    private double _nextEmptyMessageAllowedAtSeconds;
    private ulong _resolvedMuzzleTextureInstanceId;
    private bool _isFireHeld;
    private double _nextAutomaticAttemptAllowedAtSeconds;

    [Export]
    public FirearmDefinition? WeaponDefinition { get; set; }

    [Export(PropertyHint.Range, "0,999,1,or_greater")]
    public int InitialMagazineAmmo { get; set; } = 3;

    [Export(PropertyHint.Range, "0,20,1")]
    public int InitialSpareMagazineCount { get; set; }

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

        if (InitialSpareMagazineCount < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' cannot have a negative " +
                "initial spare-magazine count.");
        }

        if (definition.ReloadMechanism != FirearmReloadMechanism.DetachableMagazine &&
            InitialSpareMagazineCount != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' can only seed spare magazines " +
                "for a detachable-magazine firearm.");
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
        _weaponSprite = RequireNode<Sprite2D>("%WeaponSprite");
        _tracerLine = RequireNode<Line2D>("%TracerLine");
        EnsureMuzzlePointMatchesWeaponTexture();
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
        _state = new FirearmState(
            definition,
            InitialMagazineAmmo,
            InitialSpareMagazineCount);
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

        _isFireHeld = false;
        _rayExclusions.Clear();
        _noiseSystem = null;
        _isInitialized = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionReleased(FireAction))
        {
            _isFireHeld = false;
            _nextAutomaticAttemptAllowedAtSeconds = 0.0;
            return;
        }

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

            _isFireHeld = State.Definition.FireMode == FirearmFireMode.Automatic;
            _nextAutomaticAttemptAllowedAtSeconds = 0.0;
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

    public override void _PhysicsProcess(double delta)
    {
        if (!_isInitialized ||
            !_isCombatInputEnabled ||
            !_isFireHeld ||
            State.Definition.FireMode != FirearmFireMode.Automatic ||
            !State.CanFire)
        {
            return;
        }

        double nowSeconds = Time.GetTicksMsec() / 1000.0;
        if (nowSeconds < _nextFireAllowedAtSeconds ||
            nowSeconds < _nextAutomaticAttemptAllowedAtSeconds)
        {
            return;
        }

        FirearmShotResult result = TryFire();
        if (!result.Success)
        {
            // A blocked muzzle must not generate an attempted shot every physics frame.
            _nextAutomaticAttemptAllowedAtSeconds =
                nowSeconds + State.Definition.FireIntervalSeconds;
        }
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
            _isFireHeld = false;
            _nextAutomaticAttemptAllowedAtSeconds = 0.0;
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
            PublishShotAttempted(result);
            return result;
        }

        if (Health.IsDead)
        {
            result = FirearmShotResult.Rejected(
                FirearmShotStatus.OwnerDead,
                State.CurrentMagazineAmmo,
                "Dead actors cannot fire.");
            PublishShotAttempted(result);
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
            PublishShotAttempted(result);
            return result;
        }

        if (!TryResolveValidatedShotPath(out ValidatedShotPath shotPath))
        {
            result = FirearmShotResult.Rejected(
                FirearmShotStatus.MuzzleObstructed,
                State.CurrentMagazineAmmo,
                "Muzzle obstructed.");
            PublishShotAttempted(result);
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
        PublishShotAttempted(result);
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
            PublishReloadChanged(result);
            return result;
        }

        if (Health.IsDead)
        {
            result = ReloadResult.Rejected(
                ReloadStatus.OwnerDead,
                State.CurrentMagazineAmmo,
                "Dead actors cannot reload.");
            PublishReloadChanged(result);
            return result;
        }

        result = State.Definition.ReloadMechanism switch
        {
            FirearmReloadMechanism.DetachableMagazine =>
                State.TryBeginMagazineReload(),
            FirearmReloadMechanism.LooseRounds =>
                State.TryBeginReload(Inventory.CountByItemId(GetAmmoItemId())),
            _ => throw new InvalidOperationException(
                $"Unsupported reload mechanism '{State.Definition.ReloadMechanism}'."),
        };

        if (result.Status == ReloadStatus.Started)
        {
            _reloadTimer.Start(State.Definition.ReloadDurationSeconds);
        }
        else if (result.Status is ReloadStatus.NoReserveAmmo or ReloadStatus.NoUsableMagazine)
        {
            PublishMessage(result.Message);
        }

        PublishReloadChanged(result);
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
            PublishReloadChanged(result);
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

        ReloadResult result = State.Definition.ReloadMechanism switch
        {
            FirearmReloadMechanism.DetachableMagazine =>
                State.CompleteMagazineReload(),
            FirearmReloadMechanism.LooseRounds =>
                _reloadService.TryCompleteReload(State, Inventory, GetAmmoItemId()),
            _ => throw new InvalidOperationException(
                $"Unsupported reload mechanism '{State.Definition.ReloadMechanism}'."),
        };

        if (!result.Success)
        {
            if (State.IsReloading)
            {
                State.CancelReload();
            }

            if (result.Status is ReloadStatus.NoReserveAmmo or ReloadStatus.NoUsableMagazine)
            {
                PublishMessage(result.Message);
            }
        }

        PublishReloadChanged(result);
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
        EnsureMuzzlePointMatchesWeaponTexture();

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

        // The physical shot now begins at the currently resolved muzzle tip so
        // tracer visuals and hit detection always match the equipped weapon length.
        shotPath = new ValidatedShotPath(desiredMuzzlePosition, rayEnd);
        return true;
    }

    private void EnsureMuzzlePointMatchesWeaponTexture()
    {
        Texture2D texture = _weaponSprite.Texture
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' requires a weapon texture.");

        ulong textureInstanceId = texture.GetInstanceId();
        if (_resolvedMuzzleTextureInstanceId == textureInstanceId)
        {
            return;
        }

        _muzzlePoint.Position = ResolveWeaponLocalMuzzlePoint(texture);
        _resolvedMuzzleTextureInstanceId = textureInstanceId;
    }

    private Vector2 ResolveWeaponLocalMuzzlePoint(Texture2D texture)
    {
        Vector2 textureSize = texture.GetSize();
        if (textureSize.X <= 0.0f || textureSize.Y <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' requires a non-empty weapon texture.");
        }

        int width = Math.Max(1, (int)textureSize.X);
        int height = Math.Max(1, (int)textureSize.Y);
        if (!TryResolveOpaqueMuzzlePixel(
                texture,
                width,
                height,
                out int frontMostX,
                out float muzzleY))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController2D)} on '{Name}' cannot resolve a muzzle from a fully transparent weapon texture.");
        }

        float localX = _weaponSprite.Centered
            ? (frontMostX + 0.5f) - textureSize.X * 0.5f
            : frontMostX + 0.5f;
        float localY = _weaponSprite.Centered
            ? muzzleY - textureSize.Y * 0.5f
            : muzzleY;

        // Offset is part of Sprite2D's draw transform. MuzzlePoint is now a child of
        // WeaponSprite, so scale, rotation and left-side mirroring are inherited
        // automatically and must not be applied manually here.
        return new Vector2(localX, localY) + _weaponSprite.Offset;
    }

    private static bool TryResolveOpaqueMuzzlePixel(
        Texture2D texture,
        int width,
        int height,
        out int frontMostX,
        out float muzzleY)
    {
        frontMostX = default;
        muzzleY = default;

        Image image = texture.GetImage();
        if (image is null || image.IsEmpty())
        {
            return false;
        }

        for (int x = width - 1; x >= 0; x--)
        {
            int opaqueCount = 0;
            float ySum = 0.0f;

            for (int y = 0; y < height; y++)
            {
                Color pixel = image.GetPixel(x, y);
                if (pixel.A <= 0.01f)
                {
                    continue;
                }

                opaqueCount++;
                ySum += y + 0.5f;
            }

            if (opaqueCount == 0)
            {
                continue;
            }

            frontMostX = x;
            muzzleY = ySum / opaqueCount;
            return true;
        }

        return false;
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
            $"{State.Definition.DisplayName} gunshot");
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
        PublishShotAttempted(result);
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
        PublishMessage(result.Message);
    }

    private void PublishShotAttempted(FirearmShotResult result)
    {
        SafeEventPublisher.Publish(
            ShotAttempted,
            result,
            $"{nameof(PlayerWeaponController2D)}.{nameof(ShotAttempted)}");
    }

    private void PublishReloadChanged(ReloadResult result)
    {
        SafeEventPublisher.Publish(
            ReloadChanged,
            result,
            $"{nameof(PlayerWeaponController2D)}.{nameof(ReloadChanged)}");
    }

    private void PublishMessage(string message)
    {
        SafeEventPublisher.Publish(
            MessageRequested,
            message,
            $"{nameof(PlayerWeaponController2D)}.{nameof(MessageRequested)}");
    }

    private string GetAmmoItemId()
    {
        FirearmDefinition definition = State.Definition;
        if (definition.ReloadMechanism != FirearmReloadMechanism.LooseRounds)
        {
            throw new InvalidOperationException(
                $"{definition.DisplayName} does not reload from loose ammunition.");
        }

        return definition.AmmoItemDefinition?.Id
            ?? throw new InvalidOperationException(
                "A validated loose-round firearm definition lost its ammunition item.");
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
