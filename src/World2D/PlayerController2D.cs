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

    private Node2D _aimPivot = null!;
    private PlayerFlashlightController2D _flashlightController = null!;
    private PlayerMovementSettings _movementSettings = null!;
    private InventoryModel? _inventory;
    private HealthModel? _health;
    private StaminaModel? _stamina;
    private PlayerWeaponController2D _weaponController = null!;
    private PlayerFootstepNoiseEmitter2D _footstepNoiseEmitter = null!;
    private PlayerVisibilityController2D _visibilityController = null!;
    private NoiseSystem2D? _noiseSystem;
    private MovementMode _movementMode = MovementMode.Walk;
    private double _secondsSinceLastStaminaDrain = double.PositiveInfinity;
    private bool _isGameplayInputEnabled = true;
    private bool _isSprintRequestActive;
    private bool _sprintRequiresRelease;
    private float _firingMovementPenaltyRemainingSeconds;

    [Export]
    public PlayerMovementSettings? MovementSettings { get; set; }

    [Export(PropertyHint.Range, "0.5,1.0,0.01")]
    public float FiringMovementSpeedMultiplier { get; set; } = 0.82f;

    [Export(PropertyHint.Range, "0.05,0.5,0.01,or_greater")]
    public float FiringMovementPenaltyDurationSeconds { get; set; } = 0.12f;

    public bool IsFlashlightEnabled => _flashlightController.Model.IsOn;

    public PlayerFlashlightController2D FlashlightController => _flashlightController;

    public PlayerVisibilityController2D VisibilityController => _visibilityController;

    public bool IsGameplayInputEnabled => _isGameplayInputEnabled;

    public MovementMode CurrentMovementMode => _movementMode;

    public bool IsFiringMovementPenaltyActive =>
        _firingMovementPenaltyRemainingSeconds > 0.0f;

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

    public override void _Ready()
    {
        _movementSettings = MovementSettings
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires movement settings.");
        _movementSettings.Validate();
        ValidateFiringMovementSettings();

        _ = RequireNode<CollisionShape2D>("%NormalCollisionShape");
        if (CollisionMask == 0 || (CollisionMask & CollisionLayers2D.World) == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires the World collision mask.");
        }

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
        _weaponController.ShotFired += OnWeaponShotFired;
        _footstepNoiseEmitter.Initialize(this, _health, _movementSettings);
        _visibilityController.Initialize(
            this,
            _movementSettings,
            _flashlightController.Model,
            _health);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_weaponController))
        {
            _weaponController.ShotFired -= OnWeaponShotFired;
        }

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

    public override void _PhysicsProcess(double delta)
    {
        UpdateFiringMovementPenalty(delta);
        UpdateAimTransform();

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
        MovementMode requestedMode = isSprintRequested
            ? MovementMode.Sprint
            : MovementMode.Walk;
        float movementSpeed = GetMovementSpeed(requestedMode);
        if (IsFiringMovementPenaltyActive)
        {
            movementSpeed *= FiringMovementSpeedMultiplier;
        }

        Vector2 targetVelocity = inputDirection * movementSpeed;
        float changeRate = !hasMovementIntent
            ? _movementSettings.Deceleration
            : _movementSettings.Acceleration;

        Velocity = Velocity.MoveToward(targetVelocity, changeRate * (float)delta);

        Vector2 previousPosition = GlobalPosition;
        MoveAndSlide();
        float actualMovementDistance = GlobalPosition.DistanceTo(previousPosition);
        bool hasMeaningfulActualMovement =
            actualMovementDistance >= _movementSettings.MinimumActualMovementDistance;
        bool isActivelySprinting =
            isSprintRequested && hasMeaningfulActualMovement;

        SetMovementMode(isActivelySprinting ? MovementMode.Sprint : MovementMode.Walk);
        UpdateStamina(delta, isActivelySprinting);

        if (isActivelySprinting && Stamina.IsEmpty)
        {
            _isSprintRequestActive = false;
            _sprintRequiresRelease = true;
            SetMovementMode(MovementMode.Walk);
        }
    }

    private void UpdateAimTransform()
    {
        if (!_isGameplayInputEnabled)
        {
            return;
        }

        Viewport viewport = GetViewport();
        Rect2 visibleRect = viewport.GetVisibleRect();
        Vector2 viewportCenter = visibleRect.Position + visibleRect.Size * 0.5f;
        Vector2 aimDirection = viewport.GetMousePosition() - viewportCenter;
        if (!IsFinite(aimDirection) ||
            aimDirection.LengthSquared() <= MinimumAimDistanceSquared)
        {
            return;
        }

        // Camera2D is centered on the player. Resolve aim in screen space so camera
        // interpolation cannot introduce a frame-dependent world-space offset between
        // the mouse position and AimPivot while the player is moving.
        _aimPivot.GlobalRotation = aimDirection.Angle();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Echo: true } || !_isGameplayInputEnabled)
        {
            return;
        }

        if (@event.IsActionReleased("sprint"))
        {
            _isSprintRequestActive = false;
            _sprintRequiresRelease = false;
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
        SetMovementMode(MovementMode.Walk);
        Velocity = Vector2.Zero;
        _firingMovementPenaltyRemainingSeconds = 0.0f;
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

    private void OnWeaponShotFired()
    {
        _firingMovementPenaltyRemainingSeconds = Math.Max(
            _firingMovementPenaltyRemainingSeconds,
            FiringMovementPenaltyDurationSeconds);
    }

    private void UpdateFiringMovementPenalty(double delta)
    {
        if (_firingMovementPenaltyRemainingSeconds <= 0.0f)
        {
            _firingMovementPenaltyRemainingSeconds = 0.0f;
            return;
        }

        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        _firingMovementPenaltyRemainingSeconds = Math.Max(
            0.0f,
            _firingMovementPenaltyRemainingSeconds - (float)delta);
    }

    private void ValidateFiringMovementSettings()
    {
        if (!float.IsFinite(FiringMovementSpeedMultiplier) ||
            FiringMovementSpeedMultiplier <= 0.0f ||
            FiringMovementSpeedMultiplier > 1.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires {nameof(FiringMovementSpeedMultiplier)} " +
                "to be finite, positive, and no greater than 1.");
        }

        if (!float.IsFinite(FiringMovementPenaltyDurationSeconds) ||
            FiringMovementPenaltyDurationSeconds <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} on '{Name}' requires a positive finite " +
                $"{nameof(FiringMovementPenaltyDurationSeconds)}.");
        }
    }

    private bool CanRequestSprint(bool isSprintHeld, bool hasMovementIntent)
    {
        if (!isSprintHeld ||
            !hasMovementIntent ||
            _sprintRequiresRelease ||
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
            MovementMode.Sprint => _movementSettings.SprintSpeed,
            _ => throw new InvalidOperationException("Unknown player movement mode.")
        };
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

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
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
