using System;
using Godot;
using LineZero.Gameplay.Enemies;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Noise;
using LineZero.Gameplay.Perception;
using LineZero.World2D.Noise;

namespace LineZero.World2D.Enemies;

public sealed partial class MutantController2D : CharacterBody2D, IHealthOwner, INoiseListener2D
{
    private const float MinimumDirectionSquared = 0.0001f;
    private const double AttackTelegraphSeconds = 0.18;
    private const float InvestigationReplacementMargin = 0.35f;
    private const float InvestigationScoreDecayPerSecond = 0.2f;
    private const int MaxPerceptionChecksPerPhysicsUpdate = 3;

    private static readonly Color DamageFlashColor =
        new(1.0f, 0.62f, 0.24f, 1.0f);
    private static readonly Color AttackTelegraphColor =
        new(1.0f, 0.24f, 0.18f, 1.0f);
    private static readonly Color DeadBodyColor =
        new(0.16f, 0.18f, 0.16f, 1.0f);
    private static readonly Color DeadCoreColor =
        new(0.25f, 0.09f, 0.11f, 1.0f);

    private readonly Godot.Collections.Array<Rid> _sightRayExclusions = new();

    private MutantDefinition _definition = null!;
    private HealthModel? _health;
    private Node2D? _target;
    private HealthModel? _targetHealth;
    private IVisibilityTarget? _visibilityTarget;
    private NavigationAgent2D _navigationAgent = null!;
    private CollisionShape2D _collisionShape = null!;
    private Node2D _visualPivot = null!;
    private Marker2D _sightOrigin = null!;
    private Polygon2D _bodyVisual = null!;
    private Polygon2D _coreVisual = null!;
    private Label _healthLabel = null!;
    private Label _stateLabel = null!;
    private Timer _damageFlashTimer = null!;
    private Timer _attackWindupTimer = null!;
    private Color _aliveBodyColor;
    private Color _aliveCoreColor;
    private Vector2 _spawnPosition;
    private Vector2 _facingDirection = Vector2.Left;
    private Vector2[] _patrolPoints = Array.Empty<Vector2>();
    private Vector2 _lastKnownTargetPosition;
    private Vector2 _investigationPosition;
    private Vector2 _desiredNavigationPosition;
    private Vector2 _stuckWindowStartPosition;
    private MutantState _state = MutantState.Idle;
    private int _patrolPointIndex;
    private double _patrolWaitRemaining;
    private double _perceptionElapsed;
    private double _timeSinceLastSeen = double.PositiveInfinity;
    private double _chasePathRefreshElapsed;
    private double _stateElapsedSeconds;
    private double _investigationWaitRemaining;
    private double _stuckElapsedSeconds;
    private double _navigationRetryBlockedUntilSeconds;
    private double _nextAttackAllowedAtSeconds;
    private double _investigationAcceptedAtSeconds;
    private float _investigationPriorityScore;
    private ulong _lastProcessedNoiseSequence;
    private bool _bindingEstablished;
    private bool _isTargetingEnabled = true;
    private bool _canCurrentlySeeTarget;
    private bool _hasLastKnownTargetPosition;
    private bool _hasInvestigationTarget;
    private bool _navigationReady;
    private bool _hasDesiredNavigationPosition;
    private bool _hasActiveNavigationTarget;
    private bool _isWaitingAtPatrolPoint;
    private bool _isWaitingAtInvestigationPoint;
    private bool _isAttackPending;
    private bool _navigationPathSampled;
    private bool _navigationExpectedMovement;
    private bool _stuckRecoveryAttempted;

    [Export]
    public MutantDefinition? Definition { get; set; }

    [Export]
    public Vector2[] PatrolPointOffsets { get; set; } = Array.Empty<Vector2>();

    [Export]
    public Vector2 InitialFacingDirection { get; set; } = Vector2.Left;

    [Export(PropertyHint.Layers2DPhysics)]
    public uint SightCollisionMask { get; set; } = CollisionLayers2D.World;

    [Export]
    public bool EnableDebugStateLabel { get; set; }

    public HealthModel Health => _health
        ?? throw new InvalidOperationException(
            $"{nameof(MutantController2D)} on '{Name}' has no initialized health model.");

    public MutantState State => _state;

    public bool CanCurrentlySeeTarget => _canCurrentlySeeTarget;

    public bool HasLastKnownTargetPosition => _hasLastKnownTargetPosition;

    public Vector2 LastKnownTargetPosition => _lastKnownTargetPosition;

    public int CurrentPatrolPointIndex => _patrolPointIndex;

    public int PatrolPointCount => _patrolPoints.Length;

    public bool HasInvestigationTarget => _hasInvestigationTarget;

    public Vector2 InvestigationPosition => _investigationPosition;

    public ulong LastProcessedNoiseSequence => _lastProcessedNoiseSequence;

    Node INoiseListener.ListenerNode => this;

    public Node2D ListenerNode2D => this;

    public bool CanReceiveNoise =>
        _isTargetingEnabled &&
        _health is not null &&
        Health.IsAlive &&
        IsInsideTree();

    public float HearingSensitivity => _definition is null
        ? throw new InvalidOperationException("Mutant hearing is not initialized.")
        : _definition.HearingSensitivity;

    public float MinimumAudibleIntensity => _definition is null
        ? throw new InvalidOperationException("Mutant hearing is not initialized.")
        : _definition.MinimumAudibleIntensity;

    public event Action<MutantState, MutantState>? StateChanged;

    public event Action<DamageInfo, HealthChangeResult>? MeleeAttackApplied;

    public event Action<PerceivedNoise2D>? NoiseAccepted;

    public event Action? NavigationRepathRequested;

    public event Action<MutantState>? NavigationAbandoned;

    public override void _Ready()
    {
        _definition = Definition
            ?? throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' requires a mutant definition.");
        _definition.Validate();

        uint requiredLayers =
            CollisionLayers2D.World | CollisionLayers2D.DamageableTarget;
        if ((CollisionLayer & requiredLayers) != requiredLayers)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' must use World and " +
                "DamageableTarget collision layers.");
        }

        if ((SightCollisionMask & CollisionLayers2D.World) == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' must check World-layer sight blockers.");
        }

        if (!IsFiniteNonZero(InitialFacingDirection))
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' requires a finite initial facing direction.");
        }

        _navigationAgent = RequireNode<NavigationAgent2D>("%NavigationAgent2D");
        _collisionShape = RequireNode<CollisionShape2D>("%MutantCollision");
        _visualPivot = RequireNode<Node2D>("%VisualPivot");
        _sightOrigin = RequireNode<Marker2D>("%SightOrigin");
        _bodyVisual = RequireNode<Polygon2D>("%BodyVisual");
        _coreVisual = RequireNode<Polygon2D>("%CoreVisual");
        _healthLabel = RequireNode<Label>("%MutantHealthLabel");
        _stateLabel = RequireNode<Label>("%MutantStateLabel");
        _damageFlashTimer = RequireNode<Timer>("%DamageFlashTimer");
        _attackWindupTimer = RequireNode<Timer>("%AttackWindupTimer");

        if (_collisionShape.Shape is null || _collisionShape.Disabled)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' requires an enabled collision shape.");
        }

        if (!_damageFlashTimer.OneShot || !_attackWindupTimer.OneShot)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' requires one-shot feedback timers.");
        }

        if (_navigationAgent.PathDesiredDistance <= 0.0f ||
            _navigationAgent.TargetDesiredDistance <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' requires positive navigation tolerances.");
        }

        HealthComponent healthComponent = RequireNode<HealthComponent>("%HealthComponent");
        _health = healthComponent.Health;
        if (_health.MaxHealth != _definition.MaxHealth)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' health must match definition " +
                $"maximum {_definition.MaxHealth}.");
        }

        _spawnPosition = GlobalPosition;
        _stuckWindowStartPosition = GlobalPosition;
        _facingDirection = InitialFacingDirection.Normalized();
        _patrolPoints = CreatePatrolPoints(PatrolPointOffsets);
        _aliveBodyColor = _bodyVisual.Color;
        _aliveCoreColor = _coreVisual.Color;
        _navigationAgent.MaxSpeed = _definition.MoveSpeed;
        _navigationAgent.AvoidanceEnabled = false;
        _stateLabel.Visible = EnableDebugStateLabel;
        _visualPivot.Rotation = _facingDirection.Angle();
        _sightRayExclusions.Add(GetRid());

        _health.Changed += OnHealthChanged;
        _health.Damaged += OnDamaged;
        _health.Died += OnDied;
        _damageFlashTimer.Timeout += OnDamageFlashTimeout;
        _attackWindupTimer.Timeout += OnAttackWindupTimeout;

        RefreshHealthDisplay();
        RefreshStateDisplay();
        _perceptionElapsed = _definition.PerceptionIntervalSeconds;
        if (_patrolPoints.Length > 0)
        {
            TransitionTo(MutantState.Patrol);
        }
    }

    public override void _ExitTree()
    {
        if (_health is not null)
        {
            _health.Changed -= OnHealthChanged;
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }

        if (GodotObject.IsInstanceValid(_damageFlashTimer))
        {
            _damageFlashTimer.Timeout -= OnDamageFlashTimeout;
        }

        if (GodotObject.IsInstanceValid(_attackWindupTimer))
        {
            _attackWindupTimer.Timeout -= OnAttackWindupTimeout;
        }

        DetachTargetSubscriptions(clearReferences: true);
        _sightRayExclusions.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_bindingEstablished)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' has no explicitly bound target.");
        }

        if (_state == MutantState.Dead)
        {
            Velocity = Vector2.Zero;
            return;
        }

        _stateElapsedSeconds += delta;
        EnsureNavigationReady();
        UpdatePerception(delta);
        UpdateDecisionState();
        UpdateTimedBehavior(delta);
        UpdateMovement((float)delta);
        UpdateNavigationProgress(delta);
        UpdatePostMovementBehavior();
    }

    public void BindTarget(
        Node2D target,
        IHealthOwner healthOwner,
        IVisibilityTarget visibilityTarget)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(healthOwner);
        ArgumentNullException.ThrowIfNull(visibilityTarget);

        if (_health is null)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' is not ready for target binding.");
        }

        if (_bindingEstablished)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' already has a target binding.");
        }

        if (!GodotObject.IsInstanceValid(target) || !target.IsInsideTree())
        {
            throw new ArgumentException("The mutant target must be an active node.", nameof(target));
        }

        HealthModel targetHealth = healthOwner.Health;
        if (ReferenceEquals(target, this) || ReferenceEquals(targetHealth, Health))
        {
            throw new ArgumentException("A mutant cannot target itself.", nameof(target));
        }

        ValidateVisibilityMultiplier(
            visibilityTarget.VisibilityMultiplier,
            nameof(visibilityTarget));

        _target = target;
        _targetHealth = targetHealth;
        _visibilityTarget = visibilityTarget;
        _targetHealth.Died += OnTargetDied;
        _target.TreeExiting += OnTargetTreeExiting;
        _bindingEstablished = true;
        _perceptionElapsed = _definition.PerceptionIntervalSeconds;
    }

    public void DisableTargeting()
    {
        if (!_isTargetingEnabled)
        {
            return;
        }

        _isTargetingEnabled = false;
        CancelPendingAttack();
        DetachTargetSubscriptions(clearReferences: true);
        _canCurrentlySeeTarget = false;
        _hasLastKnownTargetPosition = false;
        _timeSinceLastSeen = double.PositiveInfinity;
        ClearInvestigation();

        if (_state != MutantState.Dead)
        {
            TransitionTo(_patrolPoints.Length > 0
                ? MutantState.Patrol
                : MutantState.Idle);
        }
    }

    public void ReceiveNoise(PerceivedNoise2D noise)
    {
        ArgumentNullException.ThrowIfNull(noise);
        ulong sequenceId = noise.Occurrence.Noise.SequenceId;
        if (sequenceId <= _lastProcessedNoiseSequence)
        {
            return;
        }

        _lastProcessedNoiseSequence = sequenceId;
        if (!CanReceiveNoise ||
            _canCurrentlySeeTarget ||
            _state == MutantState.Chase ||
            _state == MutantState.Attack)
        {
            return;
        }

        float priorityScore = CalculateNoisePriority(noise);
        if (_state == MutantState.ChaseLastKnownPosition &&
            noise.Occurrence.Noise.Kind != NoiseKind.Gunshot)
        {
            return;
        }

        if (_state == MutantState.Investigate)
        {
            double elapsed = Math.Max(
                0.0,
                noise.Occurrence.Noise.TimestampSeconds - _investigationAcceptedAtSeconds);
            float decayedCurrentScore = MathF.Max(
                0.0f,
                _investigationPriorityScore -
                ((float)elapsed * InvestigationScoreDecayPerSecond));
            if (priorityScore < decayedCurrentScore + InvestigationReplacementMargin)
            {
                return;
            }
        }

        AcceptInvestigation(noise, priorityScore);
    }

    private bool IsTargetAliveAndValid()
    {
        return _isTargetingEnabled &&
               _target is not null &&
               GodotObject.IsInstanceValid(_target) &&
               _target.IsInsideTree() &&
               _targetHealth is not null &&
               _targetHealth.IsAlive;
    }

    private void EnsureNavigationReady()
    {
        if (_navigationReady)
        {
            return;
        }

        Rid navigationMap = _navigationAgent.GetNavigationMap();
        if (!navigationMap.IsValid ||
            NavigationServer2D.MapGetIterationId(navigationMap) == 0 ||
            !NavigationServer2D.MapGetClosestPointOwner(
                navigationMap,
                GlobalPosition).IsValid)
        {
            return;
        }

        _navigationReady = true;
        ApplyDesiredNavigationPosition();
    }

    private void UpdatePerception(double delta)
    {
        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        if (!_canCurrentlySeeTarget && !double.IsPositiveInfinity(_timeSinceLastSeen))
        {
            _timeSinceLastSeen += delta;
        }

        double perceptionInterval = _definition.PerceptionIntervalSeconds;
        double accumulatedElapsed = _perceptionElapsed + delta;
        if (!double.IsFinite(accumulatedElapsed))
        {
            _perceptionElapsed = 0.0;
            PerformPerceptionCheck();
            return;
        }

        _perceptionElapsed = accumulatedElapsed;
        int checksPerformed = 0;
        while (_perceptionElapsed >= perceptionInterval &&
               checksPerformed < MaxPerceptionChecksPerPhysicsUpdate)
        {
            _perceptionElapsed -= perceptionInterval;
            PerformPerceptionCheck();
            checksPerformed++;
        }

        if (_perceptionElapsed >= perceptionInterval)
        {
            // Discard only excess whole intervals after the bounded catch-up.
            // The fractional phase is retained without scheduling a burst for future frames.
            _perceptionElapsed %= perceptionInterval;
        }
    }

    private void PerformPerceptionCheck()
    {
        bool wasSeeingTarget = _canCurrentlySeeTarget;
        bool canSeeTarget = IsTargetAliveAndValid() && IsTargetInsideSightCone() &&
            HasUnblockedLineToTarget();
        _canCurrentlySeeTarget = canSeeTarget;

        if (!canSeeTarget)
        {
            if (wasSeeingTarget && _state == MutantState.Chase)
            {
                _chasePathRefreshElapsed =
                    _definition.ChasePathRefreshIntervalSeconds;
            }

            return;
        }

        _lastKnownTargetPosition = _target!.GlobalPosition;
        _hasLastKnownTargetPosition = true;
        _timeSinceLastSeen = 0.0;
    }

    private bool IsTargetInsideSightCone()
    {
        Vector2 toTarget = _target!.GlobalPosition - GlobalPosition;
        float distanceSquared = toTarget.LengthSquared();
        float effectiveSightRange = GetEffectiveSightRange();
        if (distanceSquared > effectiveSightRange * effectiveSightRange)
        {
            return false;
        }

        if (distanceSquared <= MinimumDirectionSquared ||
            _definition.FieldOfViewDegrees >= 359.9f)
        {
            return true;
        }

        Vector2 targetDirection = toTarget / Mathf.Sqrt(distanceSquared);
        float halfFovRadians = Mathf.DegToRad(_definition.FieldOfViewDegrees * 0.5f);
        float minimumFacingDot = Mathf.Cos(halfFovRadians);
        return _facingDirection.Dot(targetDirection) >= minimumFacingDot;
    }

    private bool HasUnblockedLineToTarget()
    {
        if (!IsTargetAliveAndValid())
        {
            return false;
        }

        Vector2 rayStart = _sightOrigin.GlobalPosition;
        Vector2 rayEnd = _target!.GlobalPosition;
        PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(
            rayStart,
            rayEnd,
            SightCollisionMask);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.Exclude = _sightRayExclusions;

        Godot.Collections.Dictionary hit =
            GetWorld2D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return true;
        }

        GodotObject? collider = hit["collider"].AsGodotObject();
        if (collider is not Node colliderNode ||
            !GodotObject.IsInstanceValid(colliderNode))
        {
            return false;
        }

        return ReferenceEquals(colliderNode, _target) ||
               ReferenceEquals(colliderNode.GetParent(), _target);
    }

    private void UpdateDecisionState()
    {
        if (Health.IsDead)
        {
            TransitionTo(MutantState.Dead);
            return;
        }

        if (!IsTargetAliveAndValid())
        {
            _canCurrentlySeeTarget = false;
            if (_state != MutantState.Idle)
            {
                TransitionTo(MutantState.Idle);
            }

            return;
        }

        float distanceToTarget = GlobalPosition.DistanceTo(_target!.GlobalPosition);
        if (_canCurrentlySeeTarget)
        {
            if (distanceToTarget <= _definition.AttackRange)
            {
                TransitionTo(MutantState.Attack);
            }
            else
            {
                TransitionTo(MutantState.Chase);
            }

            return;
        }

        if ((_state == MutantState.Chase || _state == MutantState.Attack) &&
            _hasLastKnownTargetPosition)
        {
            if (_timeSinceLastSeen < _definition.LostTargetGraceSeconds)
            {
                TransitionTo(MutantState.Chase);
            }
            else
            {
                TransitionTo(MutantState.ChaseLastKnownPosition);
            }

            return;
        }

    }

    private void UpdateTimedBehavior(double delta)
    {
        switch (_state)
        {
            case MutantState.Patrol:
                UpdatePatrolWait(delta);
                break;
            case MutantState.Investigate:
                UpdateInvestigation(delta);
                break;
            case MutantState.Chase:
                UpdateChaseNavigation(delta);
                break;
            case MutantState.ChaseLastKnownPosition:
                UpdateLastKnownSearch();
                break;
            case MutantState.Attack:
                UpdateAttack();
                break;
        }
    }

    private void UpdatePatrolWait(double delta)
    {
        if (!_isWaitingAtPatrolPoint)
        {
            return;
        }

        _patrolWaitRemaining -= delta;
        if (_patrolWaitRemaining > 0.0)
        {
            return;
        }

        _isWaitingAtPatrolPoint = false;
        _patrolPointIndex = (_patrolPointIndex + 1) % _patrolPoints.Length;
        SetNavigationDestination(_patrolPoints[_patrolPointIndex]);
    }

    private void UpdateChaseNavigation(double delta)
    {
        _chasePathRefreshElapsed += delta;
        if (_chasePathRefreshElapsed < _definition.ChasePathRefreshIntervalSeconds)
        {
            return;
        }

        _chasePathRefreshElapsed = 0.0;
        if (GetNowSeconds() < _navigationRetryBlockedUntilSeconds)
        {
            return;
        }

        if (TryGetChaseDestination(out Vector2 destination))
        {
            SetNavigationDestination(destination, resetProgress: false);
        }
    }

    private void UpdateInvestigation(double delta)
    {
        if (_isWaitingAtInvestigationPoint)
        {
            _investigationWaitRemaining -= delta;
            if (_investigationWaitRemaining <= 0.0)
            {
                ReturnToDefaultBehavior();
            }

            return;
        }

        if (_stateElapsedSeconds >= _definition.MaximumSearchSeconds)
        {
            AbandonCurrentNavigation();
        }
    }

    private void UpdateLastKnownSearch()
    {
        if (_stateElapsedSeconds >= _definition.MaximumSearchSeconds)
        {
            AbandonCurrentNavigation();
        }
    }

    private void UpdateAttack()
    {
        if (!IsTargetAliveAndValid())
        {
            ReturnToDefaultBehavior();
            return;
        }

        FaceDirection(_target!.GlobalPosition - GlobalPosition);
        if (_isAttackPending)
        {
            return;
        }

        double nowSeconds = Time.GetTicksMsec() / 1000.0;
        if (nowSeconds < _nextAttackAllowedAtSeconds)
        {
            return;
        }

        _isAttackPending = true;
        _coreVisual.Color = AttackTelegraphColor;
        _visualPivot.Scale = new Vector2(1.08f, 1.08f);
        _attackWindupTimer.Start(AttackTelegraphSeconds);
    }

    private void UpdateMovement(float delta)
    {
        Vector2 desiredVelocity = Vector2.Zero;
        _navigationExpectedMovement = false;
        if (ShouldFollowNavigation() && _navigationReady && _hasDesiredNavigationPosition)
        {
            if (!_hasActiveNavigationTarget)
            {
                ApplyDesiredNavigationPosition();
            }

            if (_hasActiveNavigationTarget)
            {
                Vector2 nextPathPosition = _navigationAgent.GetNextPathPosition();
                _navigationPathSampled = true;
                if (!_navigationAgent.IsNavigationFinished())
                {
                    _navigationExpectedMovement = true;
                    Vector2 direction = nextPathPosition - GlobalPosition;
                    if (direction.LengthSquared() > MinimumDirectionSquared)
                    {
                        desiredVelocity = direction.Normalized() * _definition.MoveSpeed;
                    }
                }
            }
        }

        Velocity = Velocity.MoveToward(
            desiredVelocity,
            _definition.Acceleration * delta);
        if (desiredVelocity.LengthSquared() > MinimumDirectionSquared)
        {
            FaceDirection(desiredVelocity);
        }

        MoveAndSlide();
    }

    private bool ShouldFollowNavigation()
    {
        return _state == MutantState.Patrol && !_isWaitingAtPatrolPoint ||
               _state == MutantState.Investigate && !_isWaitingAtInvestigationPoint ||
               _state == MutantState.Chase ||
               _state == MutantState.ChaseLastKnownPosition;
    }

    private void UpdatePostMovementBehavior()
    {
        switch (_state)
        {
            case MutantState.Patrol when !_isWaitingAtPatrolPoint:
                if (HasReachedNavigationDestination(
                        _patrolPoints[_patrolPointIndex]))
                {
                    BeginPatrolWait();
                }
                else if (HasNavigationFailed())
                {
                    AbandonCurrentNavigation();
                }

                break;
            case MutantState.Investigate when !_isWaitingAtInvestigationPoint:
                if (HasReachedNavigationDestination(_investigationPosition))
                {
                    BeginInvestigationWait();
                }
                else if (HasNavigationFailed())
                {
                    AbandonCurrentNavigation();
                }

                break;
            case MutantState.ChaseLastKnownPosition:
                if (HasReachedNavigationDestination(_lastKnownTargetPosition) ||
                    HasNavigationFailed())
                {
                    AbandonCurrentNavigation();
                }

                break;
            case MutantState.Chase when HasNavigationFailed():
                AbandonCurrentNavigation();
                break;
        }
    }

    private bool HasReachedNavigationDestination(Vector2 destination)
    {
        float desiredDistance = _navigationAgent.TargetDesiredDistance;
        if (GlobalPosition.DistanceSquaredTo(destination) <= desiredDistance * desiredDistance)
        {
            return true;
        }

        return _navigationReady &&
               _hasActiveNavigationTarget &&
               _navigationPathSampled &&
               _navigationAgent.IsTargetReached();
    }

    private bool HasNavigationFailed()
    {
        return _navigationReady &&
               _hasActiveNavigationTarget &&
               _navigationPathSampled &&
               _navigationAgent.IsNavigationFinished() &&
               !_navigationAgent.IsTargetReached();
    }

    private void SetNavigationDestination(
        Vector2 position,
        bool resetProgress = true)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            throw new ArgumentException("Navigation destinations must be finite.", nameof(position));
        }

        _desiredNavigationPosition = position;
        _hasDesiredNavigationPosition = true;
        _hasActiveNavigationTarget = false;
        _navigationPathSampled = false;
        if (resetProgress)
        {
            ResetNavigationProgress(clearRecoveryAttempt: true);
        }

        ApplyDesiredNavigationPosition();
    }

    private void ApplyDesiredNavigationPosition(bool forceRefresh = false)
    {
        if (!_navigationReady || !_hasDesiredNavigationPosition)
        {
            return;
        }

        if (_hasActiveNavigationTarget && !forceRefresh)
        {
            return;
        }

        _navigationAgent.TargetPosition = _desiredNavigationPosition;
        _hasActiveNavigationTarget = true;
        _navigationPathSampled = false;
    }

    private void UpdateNavigationProgress(double delta)
    {
        if (!_navigationExpectedMovement ||
            !_hasActiveNavigationTarget ||
            !ShouldFollowNavigation())
        {
            ResetNavigationProgress(clearRecoveryAttempt: false);
            return;
        }

        float progressDistanceSquared =
            GlobalPosition.DistanceSquaredTo(_stuckWindowStartPosition);
        float requiredProgressSquared =
            _definition.MinimumProgressDistance * _definition.MinimumProgressDistance;
        if (progressDistanceSquared >= requiredProgressSquared)
        {
            ResetNavigationProgress(clearRecoveryAttempt: true);
            return;
        }

        _stuckElapsedSeconds += delta;
        if (_stuckElapsedSeconds < _definition.StuckDetectionSeconds)
        {
            return;
        }

        if (!_stuckRecoveryAttempted)
        {
            _stuckRecoveryAttempted = true;
            _stuckElapsedSeconds = 0.0;
            _stuckWindowStartPosition = GlobalPosition;
            ApplyDesiredNavigationPosition(forceRefresh: true);
            NavigationRepathRequested?.Invoke();
            return;
        }

        AbandonCurrentNavigation();
    }

    private void ResetNavigationProgress(bool clearRecoveryAttempt)
    {
        _stuckElapsedSeconds = 0.0;
        _stuckWindowStartPosition = GlobalPosition;
        if (clearRecoveryAttempt)
        {
            _stuckRecoveryAttempted = false;
        }
    }

    private void BeginPatrolWait()
    {
        _isWaitingAtPatrolPoint = true;
        _patrolWaitRemaining = _definition.PatrolWaitSeconds;
        StopNavigation();
    }

    private void BeginInvestigationWait()
    {
        _isWaitingAtInvestigationPoint = true;
        _investigationWaitRemaining = _definition.InvestigationDurationSeconds;
        StopNavigation();
    }

    private void AbandonCurrentNavigation()
    {
        MutantState abandonedState = _state;
        NavigationAbandoned?.Invoke(abandonedState);
        switch (abandonedState)
        {
            case MutantState.Patrol:
                BeginPatrolWait();
                break;
            case MutantState.Chase:
                _navigationRetryBlockedUntilSeconds =
                    GetNowSeconds() + _definition.StuckDetectionSeconds;
                StopNavigation();
                break;
            case MutantState.Investigate:
            case MutantState.ChaseLastKnownPosition:
                ReturnToDefaultBehavior();
                break;
            default:
                StopNavigation();
                break;
        }
    }

    private void StopNavigation()
    {
        _hasDesiredNavigationPosition = false;
        _hasActiveNavigationTarget = false;
        _navigationPathSampled = false;
        _navigationExpectedMovement = false;
        _desiredNavigationPosition = GlobalPosition;
        if (GodotObject.IsInstanceValid(_navigationAgent))
        {
            _navigationAgent.TargetPosition = GlobalPosition;
        }

        Velocity = Vector2.Zero;
        ResetNavigationProgress(clearRecoveryAttempt: true);
    }

    private float CalculateNoisePriority(PerceivedNoise2D noise)
    {
        float kindPriority = noise.Occurrence.Noise.Kind switch
        {
            NoiseKind.Footstep => 0.0f,
            NoiseKind.Interaction => 2.0f,
            NoiseKind.Gunshot => 5.0f,
            _ => throw new InvalidOperationException("Unknown noise kind.")
        };
        float normalizedDistance = Mathf.Clamp(
            noise.Distance / noise.EffectiveHearingRadius,
            0.0f,
            1.0f);
        float proximityScore = 1.0f - normalizedDistance;
        return kindPriority +
               (noise.PerceivedIntensity * 1.5f) +
               proximityScore;
    }

    private void AcceptInvestigation(
        PerceivedNoise2D noise,
        float priorityScore)
    {
        _investigationPosition = noise.Occurrence.WorldPosition;
        _hasInvestigationTarget = true;
        _investigationPriorityScore = priorityScore;
        _investigationAcceptedAtSeconds =
            noise.Occurrence.Noise.TimestampSeconds;
        _isWaitingAtInvestigationPoint = false;
        _investigationWaitRemaining = 0.0;

        if (_state == MutantState.ChaseLastKnownPosition)
        {
            _hasLastKnownTargetPosition = false;
            _timeSinceLastSeen = double.PositiveInfinity;
        }

        if (_state == MutantState.Investigate)
        {
            _stateElapsedSeconds = 0.0;
            SetNavigationDestination(_investigationPosition);
        }
        else
        {
            TransitionTo(MutantState.Investigate);
        }

        NoiseAccepted?.Invoke(noise);
    }

    private void ClearInvestigation()
    {
        _hasInvestigationTarget = false;
        _investigationPosition = GlobalPosition;
        _investigationPriorityScore = 0.0f;
        _investigationAcceptedAtSeconds = 0.0;
        _isWaitingAtInvestigationPoint = false;
        _investigationWaitRemaining = 0.0;
    }

    private void TransitionTo(MutantState nextState)
    {
        if (_state == nextState || _state == MutantState.Dead)
        {
            return;
        }

        MutantState previousState = _state;
        if (previousState == MutantState.Attack)
        {
            CancelPendingAttack();
        }

        if (previousState == MutantState.Investigate &&
            nextState != MutantState.Investigate)
        {
            ClearInvestigation();
        }

        _state = nextState;
        _stateElapsedSeconds = 0.0;
        switch (nextState)
        {
            case MutantState.Idle:
                StopNavigation();
                _isWaitingAtPatrolPoint = false;
                break;
            case MutantState.Patrol:
                if (_patrolPoints.Length == 0)
                {
                    throw new InvalidOperationException(
                        "A mutant without patrol points cannot enter Patrol.");
                }

                _isWaitingAtPatrolPoint = false;
                _patrolWaitRemaining = 0.0;
                SetNavigationDestination(_patrolPoints[_patrolPointIndex]);
                break;
            case MutantState.Investigate:
                if (!_hasInvestigationTarget)
                {
                    throw new InvalidOperationException(
                        "A mutant cannot investigate without a heard position.");
                }

                _isWaitingAtInvestigationPoint = false;
                _investigationWaitRemaining = 0.0;
                SetNavigationDestination(_investigationPosition);
                break;
            case MutantState.Chase:
                _chasePathRefreshElapsed = 0.0;
                if (GetNowSeconds() < _navigationRetryBlockedUntilSeconds)
                {
                    StopNavigation();
                }
                else if (TryGetChaseDestination(out Vector2 chaseDestination))
                {
                    SetNavigationDestination(chaseDestination);
                }
                else
                {
                    throw new InvalidOperationException(
                        "A mutant cannot chase without a visible or last-known target position.");
                }

                break;
            case MutantState.ChaseLastKnownPosition:
                SetNavigationDestination(_lastKnownTargetPosition);
                break;
            case MutantState.Attack:
                StopNavigation();
                Velocity = Vector2.Zero;
                break;
            case MutantState.Dead:
                EnterDeadState();
                break;
        }

        RefreshStateDisplay();
        StateChanged?.Invoke(previousState, nextState);
    }

    private void ReturnToDefaultBehavior()
    {
        _canCurrentlySeeTarget = false;
        _hasLastKnownTargetPosition = false;
        _timeSinceLastSeen = double.PositiveInfinity;
        ClearInvestigation();
        TransitionTo(_patrolPoints.Length > 0
            ? MutantState.Patrol
            : MutantState.Idle);
    }

    private void FaceDirection(Vector2 direction)
    {
        if (direction.LengthSquared() <= MinimumDirectionSquared)
        {
            return;
        }

        _facingDirection = direction.Normalized();
        _visualPivot.Rotation = _facingDirection.Angle();
    }

    private void OnAttackWindupTimeout()
    {
        if (!_isAttackPending)
        {
            return;
        }

        _isAttackPending = false;
        RestoreAttackVisual();
        if (_state != MutantState.Attack || !IsTargetAliveAndValid())
        {
            return;
        }

        float distanceToTarget = GlobalPosition.DistanceTo(_target!.GlobalPosition);
        if (distanceToTarget > _definition.AttackRange)
        {
            TransitionTo(MutantState.Chase);
            return;
        }

        if (!HasUnblockedLineToTarget())
        {
            _canCurrentlySeeTarget = false;
            if (_hasLastKnownTargetPosition)
            {
                TransitionTo(MutantState.Chase);
            }
            else
            {
                ReturnToDefaultBehavior();
            }

            return;
        }

        DamageInfo damage = new(
            _definition.AttackDamage,
            this,
            "MutantMelee");
        HealthChangeResult result = _targetHealth!.ApplyDamage(damage);
        _nextAttackAllowedAtSeconds =
            Time.GetTicksMsec() / 1000.0 + _definition.AttackCooldownSeconds;
        if (result.Changed)
        {
            MeleeAttackApplied?.Invoke(damage, result);
        }
    }

    private void CancelPendingAttack()
    {
        _isAttackPending = false;
        if (GodotObject.IsInstanceValid(_attackWindupTimer))
        {
            _attackWindupTimer.Stop();
        }

        RestoreAttackVisual();
    }

    private void RestoreAttackVisual()
    {
        if (!GodotObject.IsInstanceValid(_visualPivot))
        {
            return;
        }

        _visualPivot.Scale = Vector2.One;
        if (Health.IsAlive && GodotObject.IsInstanceValid(_coreVisual))
        {
            _coreVisual.Color = _aliveCoreColor;
        }
    }

    private void OnHealthChanged(HealthChangeResult result)
    {
        RefreshHealthDisplay();
    }

    private void OnDamaged(DamageInfo damage, HealthChangeResult result)
    {
        if (result.CausedDeath)
        {
            return;
        }

        _bodyVisual.Color = DamageFlashColor;
        _damageFlashTimer.Stop();
        _damageFlashTimer.Start();

        if (!IsDamageFromBoundTarget(damage) || !IsTargetAliveAndValid())
        {
            return;
        }

        _lastKnownTargetPosition = _target!.GlobalPosition;
        _hasLastKnownTargetPosition = true;
        _timeSinceLastSeen = 0.0;
        _canCurrentlySeeTarget = false;
        TransitionTo(MutantState.ChaseLastKnownPosition);
    }

    private bool IsDamageFromBoundTarget(DamageInfo damage)
    {
        Node? source = damage.Source;
        return source is not null &&
               GodotObject.IsInstanceValid(source) &&
               (ReferenceEquals(source, _target) ||
                ReferenceEquals(source.GetParent(), _target));
    }

    private void OnDied(DamageInfo damage, HealthChangeResult result)
    {
        TransitionTo(MutantState.Dead);
    }

    private void EnterDeadState()
    {
        CancelPendingAttack();
        _damageFlashTimer.Stop();
        StopNavigation();
        ClearInvestigation();
        Velocity = Vector2.Zero;
        _isWaitingAtPatrolPoint = false;
        _patrolWaitRemaining = 0.0;
        _perceptionElapsed = 0.0;
        _chasePathRefreshElapsed = 0.0;
        _timeSinceLastSeen = double.PositiveInfinity;
        _canCurrentlySeeTarget = false;
        _hasLastKnownTargetPosition = false;
        CollisionLayer = 0;
        CollisionMask = 0;
        _collisionShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        _navigationAgent.AvoidanceEnabled = false;
        _bodyVisual.Color = DeadBodyColor;
        _coreVisual.Color = DeadCoreColor;
        _visualPivot.Scale = Vector2.One;
        DetachTargetSubscriptions(clearReferences: true);
        RefreshHealthDisplay();
    }

    private void OnDamageFlashTimeout()
    {
        if (Health.IsAlive)
        {
            _bodyVisual.Color = _aliveBodyColor;
        }
    }

    private void OnTargetDied(DamageInfo damage, HealthChangeResult result)
    {
        _canCurrentlySeeTarget = false;
        _hasLastKnownTargetPosition = false;
        _timeSinceLastSeen = double.PositiveInfinity;
        CancelPendingAttack();
        if (_state != MutantState.Dead)
        {
            TransitionTo(MutantState.Idle);
        }
    }

    private void OnTargetTreeExiting()
    {
        DetachTargetSubscriptions(clearReferences: true);
        _canCurrentlySeeTarget = false;
        _hasLastKnownTargetPosition = false;
        CancelPendingAttack();
        if (_state != MutantState.Dead)
        {
            TransitionTo(MutantState.Idle);
        }
    }

    private void DetachTargetSubscriptions(bool clearReferences)
    {
        if (_targetHealth is not null)
        {
            _targetHealth.Died -= OnTargetDied;
        }

        if (_target is not null && GodotObject.IsInstanceValid(_target))
        {
            _target.TreeExiting -= OnTargetTreeExiting;
        }

        if (clearReferences)
        {
            _target = null;
            _targetHealth = null;
            _visibilityTarget = null;
        }
    }

    private void RefreshHealthDisplay()
    {
        if (Health.IsDead)
        {
            _healthLabel.Visible = true;
            _healthLabel.Text = "MUTANT DEAD";
            _healthLabel.Modulate = new Color(0.72f, 0.31f, 0.28f, 1.0f);
            return;
        }

        _healthLabel.Text =
            $"{_definition.DisplayName.ToUpperInvariant()} {Health.CurrentHealth} / {Health.MaxHealth}";
        _healthLabel.Visible = Health.CurrentHealth < Health.MaxHealth;
    }

    private void RefreshStateDisplay()
    {
        _stateLabel.Text = _state switch
        {
            MutantState.Idle => "IDLE",
            MutantState.Patrol => "PATROL",
            MutantState.Investigate => "INVESTIGATE",
            MutantState.Chase => "CHASE",
            MutantState.ChaseLastKnownPosition => "SEARCH",
            MutantState.Attack => "ATTACK",
            MutantState.Dead => "DEAD",
            _ => throw new InvalidOperationException("Unknown mutant state.")
        };
    }

    private Vector2[] CreatePatrolPoints(Vector2[] offsets)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        Vector2[] patrolPoints = new Vector2[offsets.Length];
        for (int index = 0; index < offsets.Length; index++)
        {
            Vector2 offset = offsets[index];
            if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            {
                throw new InvalidOperationException(
                    $"{nameof(MutantController2D)} on '{Name}' has a non-finite patrol offset.");
            }

            if (index > 0 && offset.IsEqualApprox(offsets[index - 1]))
            {
                throw new InvalidOperationException(
                    $"{nameof(MutantController2D)} on '{Name}' has duplicate consecutive patrol points.");
            }

            patrolPoints[index] = _spawnPosition + offset;
        }

        return patrolPoints;
    }

    private static bool IsFiniteNonZero(Vector2 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               value.LengthSquared() > MinimumDirectionSquared;
    }

    private float GetEffectiveSightRange()
    {
        IVisibilityTarget visibilityTarget = _visibilityTarget
            ?? throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' has no visibility target binding.");
        float multiplier = visibilityTarget.VisibilityMultiplier;
        ValidateVisibilityMultiplier(multiplier, nameof(IVisibilityTarget));

        float effectiveSightRange = _definition.SightRange * multiplier;
        if (!float.IsFinite(effectiveSightRange) || effectiveSightRange <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' calculated an invalid effective sight range.");
        }

        return effectiveSightRange;
    }

    private bool TryGetChaseDestination(out Vector2 destination)
    {
        if (_canCurrentlySeeTarget && IsTargetAliveAndValid())
        {
            destination = _target!.GlobalPosition;
            return true;
        }

        if (_hasLastKnownTargetPosition)
        {
            destination = _lastKnownTargetPosition;
            return true;
        }

        destination = GlobalPosition;
        return false;
    }

    private static void ValidateVisibilityMultiplier(
        float multiplier,
        string parameterName)
    {
        if (!float.IsFinite(multiplier) || multiplier <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Visibility multiplier must be finite and positive.");
        }
    }

    private static double GetNowSeconds()
    {
        return Time.GetTicksMsec() / 1000.0;
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(MutantController2D)} on '{Name}' requires '{path}'.");
    }
}
