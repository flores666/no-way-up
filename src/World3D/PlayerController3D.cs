using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Movement;
using LineZero.World3D.Presentation;

namespace LineZero.World3D;

public sealed partial class PlayerController3D : CharacterBody3D,
    IMovementModeSource,
    IInventoryOwner,
    IHealthOwner
{
    private const string BlockedPostureMessage =
        "There is not enough clearance to change posture.";

    private readonly Godot.Collections.Array<Rid> _clearanceExclusions = new();

    private Camera3D? _movementCamera;
    private CollisionShape3D _normalCollisionShape = null!;
    private CollisionShape3D _crouchCollisionShape = null!;
    private CollisionShape3D _crawlCollisionShape = null!;
    private PlayerVisualController3D? _visual;
    private InventoryModel? _inventory;
    private HealthModel? _health;
    private StaminaModel? _stamina;
    private MovementMode _postureMode = MovementMode.Walk;
    private MovementMode _movementMode = MovementMode.Walk;
    private double _secondsSinceLastStaminaDrain = double.PositiveInfinity;
    private bool _isGameplayInputEnabled = true;
    private bool _isTerminalState;
    private bool _isSprintRequestActive;
    private bool _sprintRequiresRelease;
    private PostureClearanceState _lastClearanceState =
        PostureClearanceState.NotRequired;

    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float WalkingSpeed { get; set; } = 5.5f;

    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float CrouchSpeed { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float CrawlSpeed { get; set; } = 1.9f;

    [Export(PropertyHint.Range, "0.1,30.0,0.1")]
    public float SprintSpeed { get; set; } = 8.5f;

    [Export(PropertyHint.Range, "0.1,80.0,0.1")]
    public float Acceleration { get; set; } = 20.0f;

    [Export(PropertyHint.Range, "0.1,80.0,0.1")]
    public float Deceleration { get; set; } = 26.0f;

    [Export(PropertyHint.Range, "0.1,80.0,0.1")]
    public float Gravity { get; set; } = 24.0f;

    [Export(PropertyHint.Range, "1.0,100.0,0.5")]
    public float TerminalFallSpeed { get; set; } = 45.0f;

    [Export(PropertyHint.Range, "0.0,2.0,0.05")]
    public float GroundSnapLength { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0")]
    public double MaximumStamina { get; set; } = 100.0;

    [Export(PropertyHint.Range, "0.01,1000.0,0.01")]
    public double SprintStaminaCostPerSecond { get; set; } = 25.0;

    [Export(PropertyHint.Range, "0.01,1000.0,0.01")]
    public double StaminaRecoveryPerSecond { get; set; } = 18.0;

    [Export(PropertyHint.Range, "0.0,30.0,0.05")]
    public double StaminaRecoveryDelaySeconds { get; set; } = 0.75;

    [Export(PropertyHint.Range, "0.0,1000.0,0.1")]
    public double MinimumStaminaToStartSprint { get; set; } = 10.0;

    public bool IsGameplayInputEnabled => _isGameplayInputEnabled;

    public bool IsTerminalState => _isTerminalState;

    public bool CanAcceptGameplayInput =>
        _isGameplayInputEnabled && !_isTerminalState;

    public bool IsUsingCrawlCollisionProfile =>
        !_crawlCollisionShape.Disabled;

    public bool IsUsingCrouchCollisionProfile =>
        !_crouchCollisionShape.Disabled;

    public Vector2 HorizontalVelocity => new(Velocity.X, Velocity.Z);

    public MovementMode CurrentMovementMode => _movementMode;

    public MovementMode CurrentPosture => _postureMode;

    public PostureClearanceState LastClearanceState =>
        _lastClearanceState;

    public StaminaModel Stamina => _stamina
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerController3D)} on '{Name}' has no stamina model.");

    public InventoryModel Inventory => _inventory
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerController3D)} on '{Name}' has no inventory model.");

    public HealthModel Health => _health
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerController3D)} on '{Name}' has no health model.");

    public PlayerVisualController3D Visual => _visual
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerController3D)} on '{Name}' has no visual adapter.");

    public event Action<MovementMode, MovementMode>? MovementModeChanged;

    public event Action<MovementMode, MovementMode>? PostureChanged;

    public event Action<PostureClearanceState>? ClearanceStateChanged;

    public event Action? InputStateChanged;

    public event Action<string>? PostureChangeRejected;

    public override void _Ready()
    {
        ValidateConfiguration();
        _normalCollisionShape = RequireNode<CollisionShape3D>(
            "%NormalCollisionShape3D");
        _crouchCollisionShape = RequireNode<CollisionShape3D>(
            "%CrouchCollisionShape3D");
        _crawlCollisionShape = RequireNode<CollisionShape3D>(
            "%CrawlCollisionShape3D");
        RequireNode<Node3D>("%VisualPivot3D");
        _visual = RequireNode<PlayerVisualController3D>("%PlayerVisual3D");
        InventoryComponent inventoryComponent =
            RequireNode<InventoryComponent>("%InventoryComponent");
        HealthComponent healthComponent =
            RequireNode<HealthComponent>("%HealthComponent");
        ValidateCollisionProfiles();

        _normalCollisionShape.Disabled = false;
        _crouchCollisionShape.Disabled = true;
        _crawlCollisionShape.Disabled = true;
        _clearanceExclusions.Clear();
        _clearanceExclusions.Add(GetRid());
        _stamina = new StaminaModel(MaximumStamina);
        _inventory = inventoryComponent.Inventory;
        _health = healthComponent.Health;
        _health.Died += OnDied;
        MotionMode = MotionModeEnum.Grounded;
        FloorSnapLength = GroundSnapLength;
        SetPhysicsProcess(false);
    }

    public override void _ExitTree()
    {
        if (_health is not null)
        {
            _health.Died -= OnDied;
        }

        _movementCamera = null;
        _clearanceExclusions.Clear();
        _inventory = null;
        _health = null;
        _stamina = null;
        _visual = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        Camera3D camera = _movementCamera
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController3D)} on '{Name}' has no movement camera.");

        float frameSeconds = float.IsFinite((float)delta) && delta > 0.0
            ? (float)delta
            : 0.0f;
        bool canMove = CanAcceptGameplayInput;
        bool isSprintHeld = canMove && Input.IsActionPressed("sprint");
        if (!isSprintHeld)
        {
            _isSprintRequestActive = false;
            _sprintRequiresRelease = false;
        }

        Vector2 input = canMove
            ? Input.GetVector("move_left", "move_right", "move_up", "move_down")
            : Vector2.Zero;
        bool hasMovementIntent = !input.IsZeroApprox();
        bool isSprintRequested = CanRequestSprint(isSprintHeld, hasMovementIntent);
        if (isSprintRequested && _postureMode != MovementMode.Walk)
        {
            if (!TryApplyPosture(
                    MovementMode.Walk,
                    notifyOnFailure: false,
                    cancelSprintRequest: false))
            {
                _isSprintRequestActive = false;
                _sprintRequiresRelease = true;
                isSprintRequested = false;
            }
        }

        MovementMode speedMode = isSprintRequested
            ? MovementMode.Sprint
            : _postureMode;
        Basis cameraBasis = camera.GlobalTransform.Basis;
        Vector3 targetVelocity = GroundMovement3D.CalculateTargetVelocity(
            input,
            -cameraBasis.Z,
            cameraBasis.X,
            GetMovementSpeed(speedMode),
            canMove);
        float changeRate = hasMovementIntent && canMove
            ? Acceleration
            : Deceleration;
        Vector3 currentVelocity = GroundMovement3D.MoveHorizontalVelocityToward(
            Velocity,
            targetVelocity,
            changeRate * frameSeconds);

        if (IsOnFloor())
        {
            currentVelocity.Y = 0.0f;
        }
        else
        {
            currentVelocity.Y = Mathf.Max(
                currentVelocity.Y - (Gravity * frameSeconds),
                -TerminalFallSpeed);
        }

        Vector3 previousPosition = GlobalPosition;
        Velocity = currentVelocity;
        MoveAndSlide();
        Vector3 displacement = GlobalPosition - previousPosition;
        displacement.Y = 0.0f;
        bool isActivelySprinting =
            isSprintRequested && displacement.LengthSquared() > 0.000001f;

        SetMovementMode(isActivelySprinting
            ? MovementMode.Sprint
            : _postureMode);
        if (!_isTerminalState)
        {
            UpdateStamina(delta, isActivelySprinting);
        }

        if (isActivelySprinting && Stamina.IsEmpty)
        {
            _isSprintRequestActive = false;
            _sprintRequiresRelease = true;
            SetMovementMode(_postureMode);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Echo: true } || !CanAcceptGameplayInput)
        {
            return;
        }

        if (@event.IsActionReleased("sprint"))
        {
            _isSprintRequestActive = false;
            _sprintRequiresRelease = false;
        }

        if (@event.IsActionPressed("stand_up"))
        {
            if (_postureMode != MovementMode.Walk)
            {
                TrySetPosture(MovementMode.Walk);
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("crouch"))
        {
            if (_postureMode != MovementMode.Crawl)
            {
                TrySetPosture(
                    _postureMode == MovementMode.Crouch
                        ? MovementMode.Walk
                        : MovementMode.Crouch);
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("crawl"))
        {
            TrySetPosture(
                _postureMode == MovementMode.Crawl
                    ? MovementMode.Crouch
                    : MovementMode.Crawl);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("sprint") &&
            _postureMode != MovementMode.Walk &&
            !_sprintRequiresRelease)
        {
            TryApplyPosture(
                MovementMode.Walk,
                notifyOnFailure: true,
                cancelSprintRequest: false);
            GetViewport().SetInputAsHandled();
        }
    }

    public void BindMovementCamera(Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (_movementCamera is not null && !ReferenceEquals(_movementCamera, camera))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerController3D)} on '{Name}' is already bound " +
                "to a different movement camera.");
        }

        _movementCamera = camera;
        SetPhysicsProcess(true);
    }

    public void SetGameplayInputEnabled(bool enabled)
    {
        bool wasEnabled = _isGameplayInputEnabled;
        _isGameplayInputEnabled = enabled &&
                                  !_isTerminalState &&
                                  (_health is null || _health.IsAlive);
        if (!_isGameplayInputEnabled)
        {
            _sprintRequiresRelease = true;
            _isSprintRequestActive = false;
            StopHorizontalMovement();
        }

        if (wasEnabled != _isGameplayInputEnabled)
        {
            SafeEventPublisher.Publish(
                InputStateChanged,
                $"{nameof(PlayerController3D)}.{nameof(InputStateChanged)}");
        }
    }

    public void SetTerminalState(bool terminal)
    {
        if (terminal)
        {
            EnterTerminalState();
        }
    }

    public void EnterTerminalState()
    {
        bool stateChanged = false;
        if (!_isTerminalState)
        {
            _isTerminalState = true;
            _isGameplayInputEnabled = false;
            _sprintRequiresRelease = true;
            _isSprintRequestActive = false;
            stateChanged = true;
        }

        // Reassert the terminal invariant without repeating any state mutation
        // or notification. Adapters may call this defensively after closing UI.
        StopHorizontalMovement();
        if (stateChanged)
        {
            SafeEventPublisher.Publish(
                InputStateChanged,
                $"{nameof(PlayerController3D)}.{nameof(InputStateChanged)}");
        }
    }

    public bool TrySetPosture(
        MovementMode nextPosture,
        bool notifyOnFailure = true)
    {
        return TryApplyPosture(
            nextPosture,
            notifyOnFailure,
            cancelSprintRequest: true);
    }

    public float GetMovementSpeed(MovementMode movementMode)
    {
        return movementMode switch
        {
            MovementMode.Walk => WalkingSpeed,
            MovementMode.Crouch => CrouchSpeed,
            MovementMode.Crawl => CrawlSpeed,
            MovementMode.Sprint => SprintSpeed,
            _ => throw new ArgumentOutOfRangeException(nameof(movementMode))
        };
    }

    private bool TryApplyPosture(
        MovementMode nextPosture,
        bool notifyOnFailure,
        bool cancelSprintRequest)
    {
        if (nextPosture == MovementMode.Sprint)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextPosture),
                "Sprint is an effective movement mode, not a posture.");
        }

        if (_isTerminalState || nextPosture == _postureMode)
        {
            return nextPosture == _postureMode;
        }

        CollisionShape3D targetCollision = GetCollisionProfile(nextPosture);
        bool needsClearance = GetPostureHeight(nextPosture) >
                              GetPostureHeight(_postureMode);
        PostureClearanceState clearanceState = needsClearance
            ? PostureClearanceState.Clear
            : PostureClearanceState.NotRequired;
        if (needsClearance && !CanCollisionProfileFit(targetCollision))
        {
            bool clearanceChanged = UpdateClearanceState(
                PostureClearanceState.Blocked);
            PublishClearanceStateChanged(clearanceChanged);
            if (notifyOnFailure)
            {
                SafeEventPublisher.Publish(
                    PostureChangeRejected,
                    BlockedPostureMessage,
                    $"{nameof(PlayerController3D)}.{nameof(PostureChangeRejected)}");
            }

            return false;
        }

        if (cancelSprintRequest)
        {
            _sprintRequiresRelease = true;
            _isSprintRequestActive = false;
        }

        MovementMode previousPosture = _postureMode;
        bool successfulClearanceChanged = UpdateClearanceState(clearanceState);
        _normalCollisionShape.Disabled =
            !ReferenceEquals(targetCollision, _normalCollisionShape);
        _crouchCollisionShape.Disabled =
            !ReferenceEquals(targetCollision, _crouchCollisionShape);
        _crawlCollisionShape.Disabled =
            !ReferenceEquals(targetCollision, _crawlCollisionShape);
        ValidateExactlyOneActiveCollisionProfile();
        SetPostureWithoutCollisionChange(nextPosture);
        PublishClearanceStateChanged(successfulClearanceChanged);
        SafeEventPublisher.Publish(
            PostureChanged,
            previousPosture,
            nextPosture,
            $"{nameof(PlayerController3D)}.{nameof(PostureChanged)}");
        return true;
    }

    private bool CanCollisionProfileFit(CollisionShape3D collisionShape)
    {
        Shape3D shape = collisionShape.Shape
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController3D)} on '{Name}' lost a posture shape.");
        PhysicsShapeQueryParameters3D query = new()
        {
            Shape = shape,
            Transform = collisionShape.GlobalTransform,
            CollisionMask = CollisionMask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = _clearanceExclusions
        };
        Godot.Collections.Array<Godot.Collections.Dictionary> overlaps =
            GetWorld3D().DirectSpaceState.IntersectShape(query, maxResults: 1);
        return overlaps.Count == 0;
    }

    private CollisionShape3D GetCollisionProfile(MovementMode posture)
    {
        return posture switch
        {
            MovementMode.Walk => _normalCollisionShape,
            MovementMode.Crouch => _crouchCollisionShape,
            MovementMode.Crawl => _crawlCollisionShape,
            _ => throw new ArgumentOutOfRangeException(nameof(posture))
        };
    }

    private static int GetPostureHeight(MovementMode posture)
    {
        return posture switch
        {
            MovementMode.Crawl => 0,
            MovementMode.Crouch => 1,
            MovementMode.Walk => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(posture))
        };
    }

    private bool UpdateClearanceState(PostureClearanceState state)
    {
        if (_lastClearanceState == state)
        {
            return false;
        }

        _lastClearanceState = state;
        return true;
    }

    private void PublishClearanceStateChanged(bool stateChanged)
    {
        if (!stateChanged)
        {
            return;
        }

        SafeEventPublisher.Publish(
            ClearanceStateChanged,
            _lastClearanceState,
            $"{nameof(PlayerController3D)}.{nameof(ClearanceStateChanged)}");
    }

    private bool CanRequestSprint(bool isSprintHeld, bool hasMovementIntent)
    {
        if (!isSprintHeld ||
            !hasMovementIntent ||
            _sprintRequiresRelease ||
            _postureMode == MovementMode.Crawl ||
            Stamina.IsEmpty)
        {
            return false;
        }

        if (_isSprintRequestActive)
        {
            return true;
        }

        if (Stamina.Current < MinimumStaminaToStartSprint)
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
            if (Stamina.Consume(SprintStaminaCostPerSecond * delta).Changed)
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
            Math.Max(previousElapsed, StaminaRecoveryDelaySeconds));
        if (recoverySeconds > 0.0)
        {
            Stamina.Restore(StaminaRecoveryPerSecond * recoverySeconds);
        }
    }

    private void SetPostureWithoutCollisionChange(MovementMode posture)
    {
        _postureMode = posture;
        Vector2 horizontal = HorizontalVelocity.LimitLength(GetMovementSpeed(posture));
        Velocity = new Vector3(horizontal.X, Velocity.Y, horizontal.Y);
        SetMovementMode(posture);
    }

    private void SetMovementMode(MovementMode nextMode)
    {
        if (_movementMode == nextMode)
        {
            return;
        }

        MovementMode previousMode = _movementMode;
        _movementMode = nextMode;
        SafeEventPublisher.Publish(
            MovementModeChanged,
            previousMode,
            nextMode,
            $"{nameof(PlayerController3D)}.{nameof(MovementModeChanged)}");
    }

    private void StopHorizontalMovement()
    {
        Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
        if (_movementMode == MovementMode.Sprint)
        {
            SetMovementMode(_postureMode);
        }
    }

    private void ValidateConfiguration()
    {
        ValidateRange(CrawlSpeed, 0.1f, 20.0f, nameof(CrawlSpeed));
        ValidateRange(CrouchSpeed, 0.1f, 20.0f, nameof(CrouchSpeed));
        ValidateRange(WalkingSpeed, 0.1f, 20.0f, nameof(WalkingSpeed));
        ValidateRange(SprintSpeed, 0.1f, 30.0f, nameof(SprintSpeed));
        if (!(CrawlSpeed < CrouchSpeed &&
              CrouchSpeed < WalkingSpeed &&
              WalkingSpeed < SprintSpeed))
        {
            throw new InvalidOperationException(
                "Movement speeds require Crawl < Crouch < Walk < Sprint.");
        }

        ValidateRange(Acceleration, 0.1f, 80.0f, nameof(Acceleration));
        ValidateRange(Deceleration, 0.1f, 80.0f, nameof(Deceleration));
        ValidateRange(Gravity, 0.1f, 80.0f, nameof(Gravity));
        ValidateRange(TerminalFallSpeed, 1.0f, 100.0f, nameof(TerminalFallSpeed));
        ValidateRange(GroundSnapLength, 0.0f, 2.0f, nameof(GroundSnapLength));
        ValidateRange(MaximumStamina, 1.0, 1000.0, nameof(MaximumStamina));
        ValidateRange(
            SprintStaminaCostPerSecond,
            0.01,
            1000.0,
            nameof(SprintStaminaCostPerSecond));
        ValidateRange(
            StaminaRecoveryPerSecond,
            0.01,
            1000.0,
            nameof(StaminaRecoveryPerSecond));
        ValidateRange(
            StaminaRecoveryDelaySeconds,
            0.0,
            30.0,
            nameof(StaminaRecoveryDelaySeconds));
        if (!double.IsFinite(MinimumStaminaToStartSprint) ||
            MinimumStaminaToStartSprint < 0.0 ||
            MinimumStaminaToStartSprint > MaximumStamina)
        {
            throw new InvalidOperationException(
                $"{nameof(MinimumStaminaToStartSprint)} must be within stamina capacity.");
        }
    }

    private void ValidateCollisionProfiles()
    {
        Shape3D normalShape = _normalCollisionShape.Shape
            ?? throw new InvalidOperationException("Normal movement shape is missing.");
        Shape3D crouchShape = _crouchCollisionShape.Shape
            ?? throw new InvalidOperationException("Crouch movement shape is missing.");
        Shape3D crawlShape = _crawlCollisionShape.Shape
            ?? throw new InvalidOperationException("Crawl movement shape is missing.");
        if (normalShape.GetRid() == crouchShape.GetRid() ||
            normalShape.GetRid() == crawlShape.GetRid() ||
            crouchShape.GetRid() == crawlShape.GetRid())
        {
            throw new InvalidOperationException(
                "Walk, Crouch, and Crawl shapes must be distinct resources.");
        }

        if (CollisionLayer != CollisionLayers3D.PlayerMovementBody ||
            (CollisionMask & CollisionLayers3D.World) == 0)
        {
            throw new InvalidOperationException(
                "Player3D movement collision layers are not configured explicitly.");
        }
    }

    private void ValidateExactlyOneActiveCollisionProfile()
    {
        int activeProfileCount = 0;
        activeProfileCount += _normalCollisionShape.Disabled ? 0 : 1;
        activeProfileCount += _crouchCollisionShape.Disabled ? 0 : 1;
        activeProfileCount += _crawlCollisionShape.Disabled ? 0 : 1;
        if (activeProfileCount != 1)
        {
            throw new InvalidOperationException(
                "Player3D must have exactly one active movement collision profile.");
        }
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController3D)} on '{Name}' requires '{path}'.");
    }

    private static void ValidateRange(
        float value,
        float minimum,
        float maximum,
        string propertyName)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{propertyName} must be between {minimum} and {maximum}.");
        }
    }

    private static void ValidateRange(
        double value,
        double minimum,
        double maximum,
        string propertyName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{propertyName} must be between {minimum} and {maximum}.");
        }
    }

    private void OnDied(DamageInfo damage, HealthChangeResult result)
    {
        EnterTerminalState();
    }
}
