using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Noise;
using LineZero.World3D.Noise;

namespace LineZero.World3D.Combat;

public sealed partial class PlayerWeaponController3D : Node3D
{
    private enum ShotPathStatus
    {
        Valid,
        InvalidAim,
        MuzzleObstructed,
    }

    private readonly record struct ValidatedShotPath(
        Vector3 SafeNoiseOrigin,
        Vector3 MuzzleOrigin,
        Vector3 RayEnd);

    private readonly record struct ResolvedHit(
        Vector3 ImpactPoint,
        Node? Collider,
        HealthModel? TargetHealth);

    private const string FireAction = "fire";
    private const string ReloadAction = "reload";
    private const float MinimumDirectionLengthSquared = 0.0001f;

    private readonly Godot.Collections.Array<Rid> _rayExclusions = new();
    private readonly FirearmReloadService _reloadService = new();
    private readonly FirearmDischargeService _dischargeService = new();

    private PlayerController3D? _player;
    private PlayerAimController3D? _aimController;
    private InventoryModel? _inventory;
    private HealthModel? _health;
    private NoiseSystem3D? _noiseSystem;
    private FirearmState? _state;
    private Marker3D _weaponOrigin = null!;
    private Marker3D _muzzlePoint = null!;
    private MeshInstance3D _tracerMesh = null!;
    private MeshInstance3D _impactMarker = null!;
    private Timer _presentationTimer = null!;
    private Timer _reloadTimer = null!;
    private bool _isInitialized;
    private bool _isCombatInputEnabled;
    private ulong _blockFireThroughProcessFrame;
    private double _nextFireAllowedAtSeconds;
    private double _nextEmptyMessageAllowedAtSeconds;

    [Export]
    public FirearmDefinition? WeaponDefinition { get; set; }

    [Export(PropertyHint.Range, "0,999,1")]
    public int InitialMagazineAmmo { get; set; } = 3;

    [Export(PropertyHint.Layers3DPhysics)]
    public uint ShotCollisionMask { get; set; } =
        CollisionLayers3D.ShotObstaclesAndTargets;

    [Export(PropertyHint.Range, "0.001,1.0,0.001")]
    public float WorldRangeScale { get; set; } = 0.04f;

    [Export(PropertyHint.Range, "0.0,2.0,0.01")]
    public float MuzzleClearanceMargin { get; set; } = 0.15f;

    [Export(PropertyHint.Range, "0.1,5.0,0.1")]
    public double EmptyMessageIntervalSeconds { get; set; } = 0.5;

    public FirearmState State => _state
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerWeaponController3D)} on '{Name}' has no firearm state.");

    public bool IsCombatInputEnabled => _isCombatInputEnabled;

    public float EffectiveRange => State.Definition.Range * WorldRangeScale;

    public FirearmShotOccurrence3D? LastShotOccurrence { get; private set; }

    public event Action<FirearmShotResult>? ShotAttempted;

    public event Action<FirearmShotOccurrence3D>? ShotResolved;

    public event Action<ReloadResult>? ReloadChanged;

    public event Action<string>? MessageRequested;

    public override void _Ready()
    {
        FirearmDefinition definition = WeaponDefinition
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController3D)} on '{Name}' requires a definition.");
        definition.Validate();
        if (InitialMagazineAmmo < 0 ||
            InitialMagazineAmmo > definition.MagazineCapacity ||
            ShotCollisionMask != CollisionLayers3D.ShotObstaclesAndTargets ||
            !float.IsFinite(WorldRangeScale) ||
            WorldRangeScale <= 0.0f ||
            !float.IsFinite(MuzzleClearanceMargin) ||
            MuzzleClearanceMargin < 0.0f ||
            !double.IsFinite(EmptyMessageIntervalSeconds) ||
            EmptyMessageIntervalSeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController3D)} on '{Name}' has invalid tuning.");
        }

        float effectiveRange = definition.Range * WorldRangeScale;
        if (!float.IsFinite(effectiveRange) ||
            effectiveRange < 1.0f ||
            effectiveRange > 200.0f)
        {
            throw new InvalidOperationException(
                "The 3D firearm effective range must be between 1 and 200 world units.");
        }

        _tracerMesh = RequireNode<MeshInstance3D>("%TracerMesh3D");
        _impactMarker = RequireNode<MeshInstance3D>("%ShotImpactMarker3D");
        _presentationTimer = RequireNode<Timer>("%ShotPresentationTimer3D");
        _reloadTimer = RequireNode<Timer>("%ReloadTimer3D");
        if (!_presentationTimer.OneShot || !_reloadTimer.OneShot)
        {
            throw new InvalidOperationException(
                "3D firearm presentation and reload timers must be one-shot.");
        }

        _presentationTimer.Timeout += OnPresentationTimerTimeout;
        _reloadTimer.Timeout += OnReloadTimerTimeout;
        _tracerMesh.Visible = false;
        _impactMarker.Visible = false;
        _state = new FirearmState(definition, InitialMagazineAmmo);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_presentationTimer))
        {
            _presentationTimer.Timeout -= OnPresentationTimerTimeout;
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
        _player = null;
        _aimController = null;
        _inventory = null;
        _health = null;
        _noiseSystem = null;
        _isInitialized = false;
        ShotAttempted = null;
        ShotResolved = null;
        ReloadChanged = null;
        MessageRequested = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Echo: true })
        {
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

            TryFire();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_isCombatInputEnabled && @event.IsActionPressed(ReloadAction))
        {
            TryBeginReload();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Initialize(
        PlayerController3D player,
        PlayerAimController3D aimController,
        InventoryModel inventory,
        HealthModel health,
        NoiseSystem3D noiseSystem,
        Marker3D weaponOrigin,
        Marker3D muzzlePoint)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(aimController);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(noiseSystem);
        ArgumentNullException.ThrowIfNull(weaponOrigin);
        ArgumentNullException.ThrowIfNull(muzzlePoint);
        if (_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController3D)} on '{Name}' is already initialized.");
        }

        if (!GodotObject.IsInstanceValid(player) ||
            !player.IsInsideTree() ||
            !ReferenceEquals(GetParent(), player) ||
            !GodotObject.IsInstanceValid(aimController) ||
            !aimController.IsInsideTree() ||
            !GodotObject.IsInstanceValid(noiseSystem) ||
            !noiseSystem.IsInsideTree() ||
            !GodotObject.IsInstanceValid(weaponOrigin) ||
            !GodotObject.IsInstanceValid(muzzlePoint) ||
            !weaponOrigin.IsInsideTree() ||
            !muzzlePoint.IsInsideTree() ||
            !player.IsAncestorOf(weaponOrigin) ||
            !player.IsAncestorOf(muzzlePoint) ||
            ReferenceEquals(weaponOrigin, muzzlePoint))
        {
            throw new ArgumentException(
                "3D firearm dependencies must be active scene nodes.");
        }

        _player = player;
        _aimController = aimController;
        _inventory = inventory;
        _health = health;
        _noiseSystem = noiseSystem;
        _weaponOrigin = weaponOrigin;
        _muzzlePoint = muzzlePoint;
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

    public FirearmDischargeResult TryFire()
    {
        EnsureInitialized();
        if (!_isCombatInputEnabled)
        {
            return PublishRejectedShot(
                FirearmShotStatus.CombatDisabled,
                "Combat input is disabled.");
        }

        if (Health.IsDead)
        {
            return PublishRejectedShot(
                FirearmShotStatus.OwnerDead,
                "Dead actors cannot fire.");
        }

        if (!State.CanFire)
        {
            FirearmShotResult stateRejection = State.TryConsumeRound();
            FirearmDischargeResult rejected = new(
                stateRejection,
                targetHealthChange: null);
            PublishShotResult(stateRejection);
            return rejected;
        }

        double nowSeconds = Time.GetTicksMsec() / 1000.0;
        if (nowSeconds < _nextFireAllowedAtSeconds)
        {
            return PublishRejectedShot(
                FirearmShotStatus.FireInterval,
                "Weapon is cycling.");
        }

        ShotPathStatus pathStatus = TryResolveValidatedShotPath(
            out ValidatedShotPath shotPath);
        if (pathStatus != ShotPathStatus.Valid)
        {
            return PublishRejectedShot(
                pathStatus == ShotPathStatus.InvalidAim
                    ? FirearmShotStatus.InvalidAim
                    : FirearmShotStatus.MuzzleObstructed,
                pathStatus == ShotPathStatus.InvalidAim
                    ? "No valid aim point."
                    : "Muzzle obstructed.");
        }

        ResolvedHit hit = ResolveFirstHit(shotPath);
        DamageInfo? damage = hit.TargetHealth is null
            ? null
            : new DamageInfo(
                State.Definition.Damage,
                Player,
                State.Definition.DisplayName);
        FirearmDischargeResult discharge = _dischargeService.TryDischarge(
            State,
            hit.TargetHealth,
            damage);
        if (!discharge.Shot.Success)
        {
            PublishShotResult(discharge.Shot);
            return discharge;
        }

        _nextFireAllowedAtSeconds =
            nowSeconds + State.Definition.FireIntervalSeconds;
        ShowShotPresentation(
            shotPath.MuzzleOrigin,
            hit.ImpactPoint,
            hit.Collider is not null);
        _noiseSystem!.EmitNoise(
            Player,
            NoiseKind.Gunshot,
            1.0f,
            shotPath.SafeNoiseOrigin,
            Player,
            "Service pistol gunshot");
        FirearmShotOccurrence3D occurrence = new(
            discharge,
            shotPath.SafeNoiseOrigin,
            shotPath.MuzzleOrigin,
            shotPath.RayEnd,
            hit.ImpactPoint,
            hit.Collider);
        LastShotOccurrence = occurrence;
        SafeEventPublisher.Publish(
            ShotResolved,
            occurrence,
            $"{nameof(PlayerWeaponController3D)}.{nameof(ShotResolved)}");
        PublishShotAttempted(discharge.Shot);
        return discharge;
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
        }
        else if (Health.IsDead)
        {
            result = ReloadResult.Rejected(
                ReloadStatus.OwnerDead,
                State.CurrentMagazineAmmo,
                "Dead actors cannot reload.");
        }
        else
        {
            result = State.TryBeginReload(
                Inventory.CountByItemId(GetAmmoItemId()));
            if (result.Status == ReloadStatus.Started)
            {
                _reloadTimer.Start(State.Definition.ReloadDurationSeconds);
            }
            else if (result.Status == ReloadStatus.NoReserveAmmo)
            {
                PublishMessage(result.Message);
            }
        }

        PublishReloadChanged(result);
        return result;
    }

    public ReloadResult CancelReload()
    {
        if (_state is null)
        {
            return ReloadResult.Rejected(
                ReloadStatus.NotReloading,
                0,
                "No reload is in progress.");
        }

        if (GodotObject.IsInstanceValid(_reloadTimer))
        {
            _reloadTimer.Stop();
        }

        ReloadResult result = State.CancelReload();
        if (result.StateChanged)
        {
            PublishReloadChanged(result);
        }

        return result;
    }

    private PlayerController3D Player => _player
        ?? throw new InvalidOperationException("3D firearm player is missing.");

    private InventoryModel Inventory => _inventory
        ?? throw new InvalidOperationException("3D firearm inventory is missing.");

    private HealthModel Health => _health
        ?? throw new InvalidOperationException("3D firearm health is missing.");

    private ShotPathStatus TryResolveValidatedShotPath(
        out ValidatedShotPath shotPath)
    {
        PlayerAimController3D aim = _aimController
            ?? throw new InvalidOperationException("3D firearm aim dependency is missing.");
        if (!aim.TryGetAimDirection(out Vector3 direction) ||
            !IsFinite(direction) ||
            direction.LengthSquared() <= MinimumDirectionLengthSquared)
        {
            shotPath = default;
            return ShotPathStatus.InvalidAim;
        }

        direction.Y = 0.0f;
        direction = direction.Normalized();
        Vector3 safeOrigin = _weaponOrigin.GlobalPosition;
        Vector3 muzzleOrigin = _muzzlePoint.GlobalPosition;
        if (!IsFinite(safeOrigin) || !IsFinite(muzzleOrigin))
        {
            shotPath = default;
            return ShotPathStatus.InvalidAim;
        }

        Vector3 authoredMuzzleDirection = muzzleOrigin - safeOrigin;
        authoredMuzzleDirection.Y = 0.0f;
        if (authoredMuzzleDirection.LengthSquared() <=
                MinimumDirectionLengthSquared ||
            authoredMuzzleDirection.Normalized().Dot(direction) < 0.8f)
        {
            shotPath = default;
            return ShotPathStatus.InvalidAim;
        }

        Vector3 clearanceEnd =
            muzzleOrigin + (direction * MuzzleClearanceMargin);
        PhysicsRayQueryParameters3D clearanceQuery =
            PhysicsRayQueryParameters3D.Create(
                safeOrigin,
                clearanceEnd,
                CollisionLayers3D.World,
                _rayExclusions);
        clearanceQuery.CollideWithAreas = false;
        clearanceQuery.CollideWithBodies = true;
        clearanceQuery.HitFromInside = true;
        if (GetWorld3D().DirectSpaceState.IntersectRay(clearanceQuery).Count > 0)
        {
            shotPath = default;
            return ShotPathStatus.MuzzleObstructed;
        }

        Vector3 rayEnd = muzzleOrigin + (direction * EffectiveRange);
        if (!IsFinite(rayEnd))
        {
            shotPath = default;
            return ShotPathStatus.InvalidAim;
        }

        shotPath = new ValidatedShotPath(
            safeOrigin,
            muzzleOrigin,
            rayEnd);
        return ShotPathStatus.Valid;
    }

    private ResolvedHit ResolveFirstHit(ValidatedShotPath shotPath)
    {
        PhysicsRayQueryParameters3D query =
            PhysicsRayQueryParameters3D.Create(
                shotPath.MuzzleOrigin,
                shotPath.RayEnd,
                ShotCollisionMask,
                _rayExclusions);
        query.CollideWithAreas = true;
        query.CollideWithBodies = true;
        query.HitFromInside = true;
        Godot.Collections.Dictionary hit =
            GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return new ResolvedHit(
                shotPath.RayEnd,
                Collider: null,
                TargetHealth: null);
        }

        Vector3 impactPoint = hit["position"].AsVector3();
        Node? collider = hit["collider"].AsGodotObject() as Node;
        IHealthOwner? healthOwner = ResolveHealthOwner(collider);
        HealthModel? targetHealth = healthOwner is not null &&
                                    !ReferenceEquals(healthOwner, Player) &&
                                    !ReferenceEquals(healthOwner.Health, Health) &&
                                    healthOwner.Health.IsAlive &&
                                    healthOwner.Health.AcceptsDamage
            ? healthOwner.Health
            : null;
        return new ResolvedHit(impactPoint, collider, targetHealth);
    }

    private static IHealthOwner? ResolveHealthOwner(Node? collider)
    {
        Node? candidate = collider;
        for (int depth = 0; depth < 3 && candidate is not null; depth++)
        {
            if (candidate is IHealthOwner healthOwner)
            {
                return healthOwner;
            }

            candidate = candidate.GetParent();
        }

        return null;
    }

    private void ShowShotPresentation(
        Vector3 rayStart,
        Vector3 impactPoint,
        bool showImpact)
    {
        Vector3 segment = impactPoint - rayStart;
        float distance = segment.Length();
        if (float.IsFinite(distance) && distance > 0.001f)
        {
            _tracerMesh.GlobalPosition = rayStart.Lerp(impactPoint, 0.5f);
            _tracerMesh.LookAt(impactPoint, Vector3.Up);
            _tracerMesh.Scale = new Vector3(1.0f, 1.0f, distance);
            _tracerMesh.Visible = true;
        }

        _impactMarker.GlobalPosition = impactPoint;
        _impactMarker.Visible = showImpact;
        _presentationTimer.Stop();
        _presentationTimer.Start();
    }

    private void OnPresentationTimerTimeout()
    {
        _tracerMesh.Visible = false;
        _impactMarker.Visible = false;
    }

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

        ReloadResult result = _reloadService.TryCompleteReload(
            State,
            Inventory,
            GetAmmoItemId());
        if (!result.Success && State.IsReloading)
        {
            State.CancelReload();
        }

        if (result.Status == ReloadStatus.NoReserveAmmo)
        {
            PublishMessage(result.Message);
        }

        PublishReloadChanged(result);
    }

    private void OnOwnerDied(DamageInfo damage, HealthChangeResult result)
    {
        SetCombatInputEnabled(false);
    }

    private FirearmDischargeResult PublishRejectedShot(
        FirearmShotStatus status,
        string message)
    {
        FirearmShotResult shot = FirearmShotResult.Rejected(
            status,
            State.CurrentMagazineAmmo,
            message);
        PublishShotResult(shot);
        return new FirearmDischargeResult(shot, targetHealthChange: null);
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
            $"{nameof(PlayerWeaponController3D)}.{nameof(ShotAttempted)}");
    }

    private void PublishReloadChanged(ReloadResult result)
    {
        SafeEventPublisher.Publish(
            ReloadChanged,
            result,
            $"{nameof(PlayerWeaponController3D)}.{nameof(ReloadChanged)}");
    }

    private void PublishMessage(string message)
    {
        SafeEventPublisher.Publish(
            MessageRequested,
            message,
            $"{nameof(PlayerWeaponController3D)}.{nameof(MessageRequested)}");
    }

    private string GetAmmoItemId()
    {
        return State.Definition.AmmoItemDefinition?.Id
            ?? throw new InvalidOperationException(
                "A validated firearm definition lost its ammunition item.");
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController3D)} on '{Name}' is not initialized.");
        }
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerWeaponController3D)} on '{Name}' requires '{path}'.");
    }
}
