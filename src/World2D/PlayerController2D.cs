using System;
using Godot;
using LineZero.Data;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Perception;
using LineZero.World2D.Combat;
using LineZero.World2D.Noise;
using LineZero.World2D.Perception;

namespace LineZero.World2D;

public sealed partial class PlayerController2D : CharacterBody2D,
    IInventoryOwner,
    IHealthOwner,
    IMovementModeSource,
    IVisibilityTarget
{
    private const float MinimumAimDistanceSquared = 0.0001f;
    private const string BlockedPostureMessage = "Cannot stand here.";

    private enum CollisionProfile
    {
        Normal,
        Crawl
    }

    private Node2D _aimPivot = null!;
    private PlayerFlashlightController2D _flashlightController = null!;
    private CollisionShape2D _normalCollisionShape = null!;
    private CollisionShape2D _crawlCollisionShape = null!;
    private PlayerMovementSettings _movementSettings = null!;
    private InventoryModel? _inventory;
    private HealthModel? _health;
    private StaminaModel? _stamina;
    private PlayerWeaponController2D _weaponController = null!;
    private PlayerFootstepNoiseEmitter2D _footstepNoiseEmitter = null!;
    private PlayerVisibilityController2D _visibilityController = null!;
    private NoiseSystem2D? _noiseSystem;
    private MovementMode _postureMode = MovementMode.Walk;
    private MovementMode _movementMode = MovementMode.Walk;
    private CollisionProfile _activeCollisionProfile = CollisionProfile.Normal;
    private CollisionProfile _requestedCollisionProfile = CollisionProfile.Normal;
    private double _secondsSinceLastStaminaDrain = double.PositiveInfinity;
    private bool _isGameplayInputEnabled = true;
    private bool _isSprintRequestActive;
    private bool _sprintRequiresRelease;
    private bool _collisionProfileApplyQueued;
    private MovementMode _requestedNormalPosture = MovementMode.Walk;
    private bool _notifyOnDeferredNormalProfileFailure;

    [Export]
    public PlayerMovementSettings? MovementSettings { get; set; }

    public bool IsFlashlightEnabled => _flashlightController.Model.IsOn;

    public PlayerFlashlightController2D FlashlightController => _flashlightController;

    public PlayerVisibilityController2D VisibilityController => _visibilityController;

    public bool IsGameplayInputEnabled => _isGameplayInputEnabled;

    public bool IsUsingCrawlCollisionProfile =>
        _activeCollisionProfile == CollisionProfile.Crawl;

    public MovementMode CurrentMovementMode => _movementMode;

    public StaminaModel Stamina => _stamina
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerController2D)} on '{Name}' has no initialized stamina model.");

    public float VisibilityMultiplier => VisibilityController.VisibilityMultiplier;

    public InventoryModel Inventory => _inventory
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerController2D)} on '{Name}' has no initialized inventory.");

    public HealthModel Health => _health
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerController2D)} on '{Name}' has no initialized health model.");

    public event Action<MovementMode, MovementMode>? MovementModeChanged;

    public event Action<string>? PostureChangeRejected;

    public override void _Ready()
    {
        _movementSettings = MovementSettings
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires movement settings.");
        _movementSettings.Validate();

        _normalCollisionShape = RequireNode<CollisionShape2D>("%NormalCollisionShape");
        _crawlCollisionShape = RequireNode<CollisionShape2D>("%CrawlCollisionShape");
        ValidateCollisionProfiles();
        _normalCollisionShape.Disabled = false;
        _crawlCollisionShape.Disabled = true;
        _activeCollisionProfile = CollisionProfile.Normal;
        _requestedCollisionProfile = CollisionProfile.Normal;
        ValidateExactlyOneActiveCollisionProfile();

        _aimPivot = RequireNode<Node2D>("%AimPivot");
        _flashlightController = RequireNode<PlayerFlashlightController2D>(
            "%PlayerFlashlightController2D");

        InventoryComponent inventoryComponent = RequireNode<InventoryComponent>(
            "%InventoryComponent");
        _inventory = inventoryComponent.Inventory;

        HealthComponent healthComponent = RequireNode<HealthComponent>("%HealthComponent");
        _health = healthComponent.Health;
        _stamina = new StaminaModel(_movementSettings.MaximumStamina);
        _health.Died += OnDied;

        _weaponController = RequireNode<PlayerWeaponController2D>(
            "%PlayerWeaponController2D");
        _footstepNoiseEmitter = RequireNode<PlayerFootstepNoiseEmitter2D>(
            "%PlayerFootstepNoiseEmitter2D");
        _visibilityController = RequireNode<PlayerVisibilityController2D>(
            "%PlayerVisibilityController2D");
        _weaponController.Initialize(this, _inventory, _health);
        _footstepNoiseEmitter.Initialize(this, _health, _movementSettings);
        _visibilityController.Initialize(
            this,
            _movementSettings,
            _flashlightController.Model,
            _health);
    }

    public override void _ExitTree()
    {
        if (_health is not null)
        {
            _health.Died -= OnDied;
        }

        if (_noiseSystem is not null && GodotObject.IsInstanceValid(_noiseSystem))
        {
            _weaponController.UnbindNoiseSystem(_noiseSystem);
            _footstepNoiseEmitter.UnbindNoiseSystem(_noiseSystem);
        }

        _noiseSystem = null;
    }

    public override void _Process(double delta)
    {
        if (!_isGameplayInputEnabled)
        {
            return;
        }

        Vector2 aimDirection = GetGlobalMousePosition() - GlobalPosition;
        if (aimDirection.LengthSquared() > MinimumAimDistanceSquared)
        {
            _aimPivot.GlobalRotation = aimDirection.Angle();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        bool isAlive = _health is not null && _health.IsAlive;
        if (!_isGameplayInputEnabled || !isAlive)
        {
            if (isAlive)
            {
                UpdateStamina(delta, isActivelySprinting: false);
            }

            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        if (_collisionProfileApplyQueued)
        {
            UpdateStamina(delta, isActivelySprinting: false);
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        bool isSprintHeld = Input.IsActionPressed("sprint");
        if (!isSprintHeld)
        {
            _isSprintRequestActive = false;
            _sprintRequiresRelease = false;
        }

        Vector2 inputDirection = Input.GetVector(
            "move_left",
            "move_right",
            "move_up",
            "move_down");
        if (inputDirection.LengthSquared() > 1.0f)
        {
            inputDirection = inputDirection.Normalized();
        }

        bool hasMovementIntent = !inputDirection.IsZeroApprox();
        bool isSprintRequested = CanRequestSprint(isSprintHeld, hasMovementIntent);
        if (isSprintRequested && _postureMode == MovementMode.Crouch)
        {
            _postureMode = MovementMode.Walk;
        }

        MovementMode speedMode = isSprintRequested
            ? MovementMode.Sprint
            : _postureMode;
        float movementSpeed = GetMovementSpeed(speedMode);
        Vector2 targetVelocity = inputDirection * movementSpeed;
        float changeRate = !hasMovementIntent
            ? _movementSettings.Deceleration
            : _movementSettings.Acceleration;

        Velocity = Velocity.MoveToward(targetVelocity, changeRate * (float)delta);
        if (speedMode == MovementMode.Crawl)
        {
            Velocity = Velocity.LimitLength(_movementSettings.CrawlSpeed);
        }

        Vector2 previousPosition = GlobalPosition;
        MoveAndSlide();
        float actualMovementDistance = GlobalPosition.DistanceTo(previousPosition);
        bool hasMeaningfulActualMovement =
            actualMovementDistance >= _movementSettings.MinimumActualMovementDistance;
        bool isActivelySprinting =
            isSprintRequested && hasMeaningfulActualMovement;

        MovementMode effectiveMode = isActivelySprinting
            ? MovementMode.Sprint
            : _postureMode;
        SetMovementMode(effectiveMode);
        UpdateStamina(delta, isActivelySprinting);

        if (isActivelySprinting && Stamina.IsEmpty)
        {
            _isSprintRequestActive = false;
            _sprintRequiresRelease = true;
            _postureMode = MovementMode.Walk;
            SetMovementMode(MovementMode.Walk);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Echo: true })
        {
            return;
        }

        if (!_isGameplayInputEnabled)
        {
            return;
        }

        if (@event.IsActionReleased("sprint"))
        {
            _isSprintRequestActive = false;
            _sprintRequiresRelease = false;
        }

        if (@event.IsActionPressed("crouch"))
        {
            HandleCrouchToggle();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("crawl"))
        {
            HandleCrawlToggle();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("sprint") &&
            _postureMode == MovementMode.Crawl &&
            !_sprintRequiresRelease)
        {
            TryExitCrawl(MovementMode.Walk, notifyOnFailure: true);
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetGameplayInputEnabled(bool enabled)
    {
        bool canEnable = enabled && (_health is null || _health.IsAlive);
        _isGameplayInputEnabled = canEnable;
        if (canEnable)
        {
            return;
        }

        _sprintRequiresRelease = true;
        _isSprintRequestActive = false;
        if (_movementMode == MovementMode.Sprint)
        {
            SetMovementMode(_postureMode);
        }

        Velocity = Vector2.Zero;
    }

    public void SetPlayerNoiseEnabled(bool enabled)
    {
        _footstepNoiseEmitter.SetEmissionEnabled(enabled);
    }

    public void BindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' already has a noise system.");
        }

        if (!GodotObject.IsInstanceValid(noiseSystem) || !noiseSystem.IsInsideTree())
        {
            throw new ArgumentException("The noise system must be active.", nameof(noiseSystem));
        }

        _weaponController.BindNoiseSystem(noiseSystem);
        _footstepNoiseEmitter.BindNoiseSystem(noiseSystem);
        _noiseSystem = noiseSystem;
    }

    private void HandleCrouchToggle()
    {
        if (_postureMode == MovementMode.Crawl)
        {
            return;
        }

        _sprintRequiresRelease = true;
        _isSprintRequestActive = false;
        MovementMode nextPosture = _postureMode == MovementMode.Crouch
            ? MovementMode.Walk
            : MovementMode.Crouch;
        SetPostureMode(nextPosture);
    }

    private void HandleCrawlToggle()
    {
        if (_postureMode == MovementMode.Crawl)
        {
            TryExitCrawl(MovementMode.Crouch, notifyOnFailure: true);
            return;
        }

        _sprintRequiresRelease = true;
        _isSprintRequestActive = false;
        _postureMode = MovementMode.Crawl;
        Velocity = Velocity.LimitLength(_movementSettings.CrawlSpeed);
        RequestCollisionProfile(CollisionProfile.Crawl);
        SetMovementMode(MovementMode.Crawl);
    }

    private bool TryExitCrawl(MovementMode nextPosture, bool notifyOnFailure)
    {
        if (_postureMode != MovementMode.Crawl)
        {
            return true;
        }

        if (nextPosture is MovementMode.Crawl or MovementMode.Sprint)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextPosture),
                nextPosture,
                "A crawl exit must select a normal collision posture.");
        }

        if (!CanNormalCollisionFit())
        {
            if (notifyOnFailure)
            {
                PostureChangeRejected?.Invoke(BlockedPostureMessage);
            }

            return false;
        }

        _requestedNormalPosture = nextPosture;
        _notifyOnDeferredNormalProfileFailure = notifyOnFailure;
        RequestCollisionProfile(CollisionProfile.Normal);
        return true;
    }

    private bool CanNormalCollisionFit()
    {
        Shape2D normalShape = _normalCollisionShape.Shape
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' lost its normal collision shape.");

        PhysicsShapeQueryParameters2D query = new()
        {
            Shape = normalShape,
            Transform = _normalCollisionShape.GlobalTransform,
            CollisionMask = CollisionMask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { GetRid() }
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> overlaps =
            GetWorld2D().DirectSpaceState.IntersectShape(query, maxResults: 1);
        return overlaps.Count == 0;
    }

    private bool CanRequestSprint(bool isSprintHeld, bool hasMovementIntent)
    {
        if (!isSprintHeld ||
            !hasMovementIntent ||
            _sprintRequiresRelease ||
            _postureMode == MovementMode.Crawl ||
            Stamina.Current <= 0.0)
        {
            return false;
        }

        if (_isSprintRequestActive)
        {
            return true;
        }

        if (Stamina.Current < _movementSettings.MinimumStaminaToStartSprint)
        {
            return false;
        }

        _isSprintRequestActive = true;
        return true;
    }

    private void UpdateStamina(double delta, bool isActivelySprinting)
    {
        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        if (isActivelySprinting)
        {
            StaminaChangeResult result = Stamina.Consume(
                _movementSettings.SprintStaminaCostPerSecond * delta);
            if (result.Changed)
            {
                _secondsSinceLastStaminaDrain = 0.0;
            }

            return;
        }

        if (double.IsPositiveInfinity(_secondsSinceLastStaminaDrain) ||
            Stamina.IsFull)
        {
            return;
        }

        double previousElapsed = _secondsSinceLastStaminaDrain;
        _secondsSinceLastStaminaDrain += delta;
        double recoverySeconds = Math.Max(
            0.0,
            _secondsSinceLastStaminaDrain -
            Math.Max(previousElapsed, _movementSettings.StaminaRecoveryDelaySeconds));
        if (recoverySeconds <= 0.0)
        {
            return;
        }

        Stamina.Restore(
            _movementSettings.StaminaRecoveryPerSecond * recoverySeconds);
    }

    private float GetMovementSpeed(MovementMode movementMode)
    {
        return movementMode switch
        {
            MovementMode.Walk => _movementSettings.WalkSpeed,
            MovementMode.Crouch => _movementSettings.CrouchSpeed,
            MovementMode.Sprint => _movementSettings.SprintSpeed,
            MovementMode.Crawl => _movementSettings.CrawlSpeed,
            _ => throw new InvalidOperationException("Unknown player movement mode.")
        };
    }

    private void SetPostureMode(MovementMode nextPosture)
    {
        if (nextPosture == MovementMode.Sprint)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextPosture),
                nextPosture,
                "Sprint is an effective movement mode, not a persistent posture.");
        }

        _postureMode = nextPosture;
        Velocity = Velocity.LimitLength(GetMovementSpeed(nextPosture));
        SetMovementMode(nextPosture);
    }

    private void SetMovementMode(MovementMode nextMode)
    {
        if (_movementMode == nextMode)
        {
            return;
        }

        MovementMode previousMode = _movementMode;
        _movementMode = nextMode;
        MovementModeChanged?.Invoke(previousMode, nextMode);
    }

    private void RequestCollisionProfile(CollisionProfile collisionProfile)
    {
        _requestedCollisionProfile = collisionProfile;
        if (_collisionProfileApplyQueued)
        {
            return;
        }

        _collisionProfileApplyQueued = true;
        Callable.From(ApplyRequestedCollisionProfile).CallDeferred();
    }

    private void ApplyRequestedCollisionProfile()
    {
        _collisionProfileApplyQueued = false;
        if (!GodotObject.IsInstanceValid(_normalCollisionShape) ||
            !GodotObject.IsInstanceValid(_crawlCollisionShape))
        {
            return;
        }

        if (_requestedCollisionProfile == CollisionProfile.Crawl)
        {
            ActivateCrawlCollisionProfile();
            _activeCollisionProfile = CollisionProfile.Crawl;
            _notifyOnDeferredNormalProfileFailure = false;
            return;
        }

        if (!CanNormalCollisionFit())
        {
            RestoreCrawlAfterBlockedDeferredExit();
            return;
        }

        _normalCollisionShape.Disabled = false;
        _crawlCollisionShape.Disabled = true;
        _activeCollisionProfile = CollisionProfile.Normal;
        _requestedCollisionProfile = CollisionProfile.Normal;
        _notifyOnDeferredNormalProfileFailure = false;
        SetPostureMode(_requestedNormalPosture);
        ValidateExactlyOneActiveCollisionProfile();
    }

    private void ActivateCrawlCollisionProfile()
    {
        _crawlCollisionShape.Disabled = false;
        _normalCollisionShape.Disabled = true;
        ValidateExactlyOneActiveCollisionProfile();
    }

    private void RestoreCrawlAfterBlockedDeferredExit()
    {
        ActivateCrawlCollisionProfile();
        _activeCollisionProfile = CollisionProfile.Crawl;
        _requestedCollisionProfile = CollisionProfile.Crawl;
        _postureMode = MovementMode.Crawl;
        Velocity = Velocity.LimitLength(_movementSettings.CrawlSpeed);
        SetMovementMode(MovementMode.Crawl);

        bool shouldNotify = _notifyOnDeferredNormalProfileFailure;
        _notifyOnDeferredNormalProfileFailure = false;
        if (shouldNotify)
        {
            PostureChangeRejected?.Invoke(BlockedPostureMessage);
        }
    }

    private void ValidateExactlyOneActiveCollisionProfile()
    {
        if (_normalCollisionShape.Disabled == _crawlCollisionShape.Disabled)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' must have exactly one active " +
                "movement collision profile.");
        }
    }

    private void ValidateCollisionProfiles()
    {
        Shape2D normalShape = _normalCollisionShape.Shape
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires a normal Shape2D.");
        Shape2D crawlShape = _crawlCollisionShape.Shape
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires a crawl Shape2D.");

        if (ReferenceEquals(normalShape, crawlShape) ||
            normalShape.GetRid() == crawlShape.GetRid())
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires distinct normal and crawl " +
                "shape resources.");
        }

        if (CollisionMask == 0 || (CollisionMask & CollisionLayers2D.World) == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires the World collision mask.");
        }
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires '{path}'.");
    }

    private void OnDied(DamageInfo damage, HealthChangeResult result)
    {
        SetGameplayInputEnabled(false);
    }
}
