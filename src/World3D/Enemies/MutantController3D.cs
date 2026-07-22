using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Enemies;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Noise;
using LineZero.Gameplay.Perception;
using LineZero.Gameplay.Timing;
using LineZero.World3D.Noise;

namespace LineZero.World3D.Enemies;

public sealed partial class MutantController3D : CharacterBody3D,
    IHealthOwner,
    INoiseListener3D
{
    private const int MaximumPerceptionChecksPerPhysicsUpdate = 3;
    private const int MaximumChaseRefreshesPerPhysicsUpdate = 2;
    private const float MinimumDirectionSquared = 0.0001f;
    private const float NavigationDestinationThresholdSquared = 0.25f;

    private readonly Godot.Collections.Array<Rid> _sightRayExclusions = new();

    private MutantDefinition _definition = null!;
    private HealthModel? _health;
    private Node3D? _target;
    private HealthModel? _targetHealth;
    private IVisibilityStateSource? _visibilityTarget;
    private NoiseSystem3D? _noiseSystem;
    private NavigationAgent3D _navigationAgent = null!;
    private CollisionShape3D _collisionShape = null!;
    private Node3D _visualPivot = null!;
    private Marker3D _sightOrigin = null!;
    private Label3D _stateLabel = null!;
    private Label3D _healthLabel = null!;
    private Timer _attackWindupTimer = null!;
    private PeriodicCatchUpTimer? _perceptionTimer;
    private PeriodicCatchUpTimer? _chaseRefreshTimer;
    private Vector3[] _patrolPoints = Array.Empty<Vector3>();
    private Vector3 _facingDirection = Vector3.Left;
    private Vector3 _lastKnownTargetPosition;
    private Vector3 _investigationPosition;
    private Vector3 _navigationDestination;
    private MutantState _state = MutantState.Idle;
    private PerceivedNoise3D? _pendingNoise;
    private int _patrolPointIndex;
    private double _timeSinceLastSeen = double.PositiveInfinity;
    private double _stateElapsedSeconds;
    private double _patrolWaitElapsedSeconds;
    private double _nextAttackAllowedAtSeconds;
    private float _pendingNoiseScore;
    private ulong _lastProcessedNoiseSequence;
    private bool _isBound;
    private bool _isTargetingEnabled = true;
    private bool _isTerminal;
    private bool _canCurrentlySeeTarget;
    private bool _hasConfirmedTargetMemory;
    private bool _hasLastKnownTargetPosition;
    private bool _hasInvestigationTarget;
    private bool _hasNavigationDestination;
    private bool _navigationReady;
    private bool _isWaitingAtPatrolPoint;
    private bool _isAttackPending;

    [Export]
    public MutantDefinition? Definition { get; set; }

    [Export]
    public Vector3[] PatrolPointOffsets { get; set; } = Array.Empty<Vector3>();

    [Export]
    public Vector3 InitialFacingDirection { get; set; } = Vector3.Left;

    [Export(PropertyHint.Range, "0.001,1.0,0.001")]
    public float WorldDistanceScale { get; set; } = 0.04f;

    [Export(PropertyHint.Range, "0.1,80.0,0.1")]
    public float Gravity { get; set; } = 24.0f;

    [Export(PropertyHint.Layers3DPhysics)]
    public uint SightCollisionMask { get; set; } = CollisionLayers3D.World;

    [Export]
    public bool EnableDebugStateLabel { get; set; }

    [Export]
    public bool EnableDebugHealthLabel { get; set; }

    public HealthModel Health => _health
        ?? throw new InvalidOperationException(
            $"{nameof(MutantController3D)} on '{Name}' has no health model.");

    public MutantState State => _state;

    public bool CanCurrentlySeeTarget => _canCurrentlySeeTarget;

    public bool HasLastKnownTargetPosition => _hasLastKnownTargetPosition;

    public Vector3 LastKnownTargetPosition => _lastKnownTargetPosition;

    public bool HasInvestigationTarget => _hasInvestigationTarget;

    public Vector3 InvestigationPosition => _investigationPosition;

    public ulong LastProcessedNoiseSequence => _lastProcessedNoiseSequence;

    public bool IsTargetingEnabled => _isTargetingEnabled && !_isTerminal;

    public Node ListenerNode => this;

    public Node3D ListenerNode3D => this;

    public bool CanReceiveNoise =>
        IsTargetingEnabled &&
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

    public event Action<PerceivedNoise3D>? NoiseAccepted;

    public override void _Ready()
    {
        _definition = Definition
            ?? throw new InvalidOperationException(
                $"{nameof(MutantController3D)} on '{Name}' requires a definition.");
        _definition.Validate();
        if (!float.IsFinite(WorldDistanceScale) ||
            WorldDistanceScale <= 0.0f ||
            !float.IsFinite(Gravity) ||
            Gravity <= 0.0f ||
            SightCollisionMask != CollisionLayers3D.World ||
            CollisionLayer !=
                (CollisionLayers3D.MutantMovementBody |
                 CollisionLayers3D.FirearmTarget) ||
            CollisionMask !=
                (CollisionLayers3D.World |
                 CollisionLayers3D.PlayerMovementBody) ||
            !IsFiniteHorizontalDirection(InitialFacingDirection))
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController3D)} on '{Name}' has invalid 3D tuning.");
        }

        _navigationAgent = RequireNode<NavigationAgent3D>("%NavigationAgent3D");
        _collisionShape = RequireNode<CollisionShape3D>("%MutantCollision3D");
        _visualPivot = RequireNode<Node3D>("%MutantVisualPivot3D");
        _sightOrigin = RequireNode<Marker3D>("%MutantSightOrigin3D");
        _stateLabel = RequireNode<Label3D>("%MutantStateLabel3D");
        _healthLabel = RequireNode<Label3D>("%MutantHealthLabel3D");
        _attackWindupTimer = RequireNode<Timer>("%MutantAttackWindupTimer3D");
        if (_collisionShape.Shape is null ||
            _collisionShape.Disabled ||
            !_attackWindupTimer.OneShot ||
            _navigationAgent.PathDesiredDistance <= 0.0f ||
            _navigationAgent.TargetDesiredDistance <= 0.0f)
        {
            throw new InvalidOperationException(
                "Mutant3D requires valid collision, navigation, and timer nodes.");
        }

        HealthComponent healthComponent =
            RequireNode<HealthComponent>("%HealthComponent");
        _health = healthComponent.Health;
        if (_health.MaxHealth != _definition.MaxHealth)
        {
            throw new InvalidOperationException(
                "Mutant3D health must match its definition.");
        }

        _facingDirection = Flatten(InitialFacingDirection).Normalized();
        _patrolPoints = CreatePatrolPoints(PatrolPointOffsets);
        _navigationAgent.MaxSpeed = Scaled(_definition.MoveSpeed);
        _navigationAgent.AvoidanceEnabled = false;
        _perceptionTimer = new PeriodicCatchUpTimer(
            _definition.PerceptionIntervalSeconds,
            MaximumPerceptionChecksPerPhysicsUpdate);
        _chaseRefreshTimer = new PeriodicCatchUpTimer(
            _definition.ChasePathRefreshIntervalSeconds,
            MaximumChaseRefreshesPerPhysicsUpdate);
        _sightRayExclusions.Add(GetRid());
        FaceDirection(_facingDirection);

        _health.Changed += OnHealthChanged;
        _health.Damaged += OnDamaged;
        _health.Died += OnDied;
        _attackWindupTimer.Timeout += OnAttackWindupTimeout;
        RefreshHealthDisplay();
        RefreshStateDisplay();
        SetPhysicsProcess(false);
    }

    public override void _ExitTree()
    {
        if (_health is not null)
        {
            _health.Changed -= OnHealthChanged;
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }

        if (_targetHealth is not null)
        {
            _targetHealth.Died -= OnTargetDied;
        }

        if (_target is not null && GodotObject.IsInstanceValid(_target))
        {
            _target.TreeExiting -= OnTargetTreeExiting;
        }

        if (_noiseSystem is not null &&
            GodotObject.IsInstanceValid(_noiseSystem))
        {
            _noiseSystem.UnregisterListener(this);
        }

        if (GodotObject.IsInstanceValid(_attackWindupTimer))
        {
            _attackWindupTimer.Timeout -= OnAttackWindupTimeout;
        }

        _sightRayExclusions.Clear();
        _target = null;
        _targetHealth = null;
        _visibilityTarget = null;
        _noiseSystem = null;
        _isBound = false;
        StateChanged = null;
        MeleeAttackApplied = null;
        NoiseAccepted = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isBound)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController3D)} on '{Name}' has no target binding.");
        }

        if (_state == MutantState.Dead || _isTerminal)
        {
            StopMovement();
            return;
        }

        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        _stateElapsedSeconds += delta;
        if (!_canCurrentlySeeTarget &&
            !double.IsPositiveInfinity(_timeSinceLastSeen))
        {
            _timeSinceLastSeen += delta;
        }

        EnsureNavigationReady();
        PeriodicCatchUpResult perception = _perceptionTimer!.Advance(delta);
        for (int check = 0; check < perception.DueTicks; check++)
        {
            PerformPerceptionCheck();
        }

        UpdateDecisionState();
        UpdateTimedState(delta);
        UpdateChaseDestination(delta);
        UpdateMovement((float)delta);
    }

    public void BindTarget(
        Node3D target,
        HealthModel targetHealth,
        IVisibilityStateSource visibilityTarget,
        NoiseSystem3D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetHealth);
        ArgumentNullException.ThrowIfNull(visibilityTarget);
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_isBound)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantController3D)} on '{Name}' is already bound.");
        }

        if (_health is null ||
            !GodotObject.IsInstanceValid(target) ||
            !target.IsInsideTree() ||
            ReferenceEquals(target, this) ||
            ReferenceEquals(targetHealth, Health) ||
            !GodotObject.IsInstanceValid(noiseSystem) ||
            !noiseSystem.IsInsideTree())
        {
            throw new ArgumentException(
                "Mutant3D target dependencies must be distinct active objects.");
        }

        _target = target;
        _targetHealth = targetHealth;
        _visibilityTarget = visibilityTarget;
        _noiseSystem = noiseSystem;
        _targetHealth.Died += OnTargetDied;
        _target.TreeExiting += OnTargetTreeExiting;
        _noiseSystem.RegisterListener(this);
        _isBound = true;
        PerformPerceptionCheck();
        UpdateDecisionState();
        SetPhysicsProcess(true);
    }

    public void DisableTargeting()
    {
        if (_isTerminal)
        {
            return;
        }

        _isTerminal = true;
        _isTargetingEnabled = false;
        _canCurrentlySeeTarget = false;
        _hasConfirmedTargetMemory = false;
        _hasLastKnownTargetPosition = false;
        _hasInvestigationTarget = false;
        _pendingNoise = null;
        CancelPendingAttack();
        TransitionTo(MutantState.Dead);
        SetPhysicsProcess(false);
    }

    public void ReceiveNoise(PerceivedNoise3D noise)
    {
        ArgumentNullException.ThrowIfNull(noise);
        ulong sequenceId = noise.Occurrence.Noise.SequenceId;
        if (sequenceId <= _lastProcessedNoiseSequence)
        {
            return;
        }

        _lastProcessedNoiseSequence = sequenceId;
        if (!CanReceiveNoise || !IsTargetAliveAndValid())
        {
            return;
        }

        bool fromTarget = IsNoiseFromTarget(noise.Occurrence.Noise.Source);
        if ((_state == MutantState.Chase || _state == MutantState.Attack) &&
            HasRecentConfirmedTargetMemory())
        {
            if (fromTarget)
            {
                _lastKnownTargetPosition = noise.Occurrence.WorldPosition;
                _hasConfirmedTargetMemory = true;
                _hasLastKnownTargetPosition = true;
                _timeSinceLastSeen = 0.0;
            }
            else
            {
                RememberNoise(noise);
            }

            PublishNoiseAccepted(noise);
            return;
        }

        RememberNoise(noise);
        PublishNoiseAccepted(noise);
    }

    private void RememberNoise(PerceivedNoise3D noise)
    {
        float score = CalculateNoiseScore(noise);
        if (_pendingNoise is null ||
            score > _pendingNoiseScore ||
            (Mathf.IsEqualApprox(score, _pendingNoiseScore) &&
             noise.Occurrence.Noise.SequenceId <
             _pendingNoise.Occurrence.Noise.SequenceId))
        {
            _pendingNoise = noise;
            _pendingNoiseScore = score;
            _investigationPosition = noise.Occurrence.WorldPosition;
            _hasInvestigationTarget = true;
        }
    }

    private void PerformPerceptionCheck()
    {
        bool canSee = IsTargetAliveAndValid() &&
                      IsTargetInsideSightCone() &&
                      HasUnblockedLineToTarget();
        _canCurrentlySeeTarget = canSee;
        if (!canSee)
        {
            return;
        }

        _lastKnownTargetPosition = _target!.GlobalPosition;
        _hasConfirmedTargetMemory = true;
        _hasLastKnownTargetPosition = true;
        _timeSinceLastSeen = 0.0;
        _pendingNoise = null;
        _pendingNoiseScore = 0.0f;
        _hasInvestigationTarget = false;
    }

    private void UpdateDecisionState()
    {
        bool targetAlive = IsTargetAliveAndValid();
        MutantDecisionContext context = new(
            IsAlive: Health.IsAlive,
            IsTerminal: _isTerminal,
            IsTargetAlive: targetAlive,
            CanSeeTarget: _canCurrentlySeeTarget,
            IsTargetInAttackRange:
                targetAlive && IsTargetInsideAttackRange(),
            HasChaseGrace:
                targetAlive && HasRecentConfirmedTargetMemory(),
            HasLastKnownTarget:
                targetAlive && _hasLastKnownTargetPosition,
            HasRelevantNoise:
                targetAlive && _hasInvestigationTarget,
            IsSearching:
                _state == MutantState.ChaseLastKnownPosition &&
                _stateElapsedSeconds < _definition.MaximumSearchSeconds,
            HasPatrolRoute: _patrolPoints.Length > 0);
        TransitionTo(MutantDecisionRules.Decide(context));
    }

    private void UpdateTimedState(double delta)
    {
        switch (_state)
        {
            case MutantState.Patrol:
                UpdatePatrol(delta);
                break;
            case MutantState.Investigate:
                if (HasReachedDestination())
                {
                    StopMovement();
                    if (_stateElapsedSeconds >=
                        _definition.InvestigationDurationSeconds)
                    {
                        ClearInvestigation();
                        UpdateDecisionState();
                    }
                }
                else if (_stateElapsedSeconds >=
                         _definition.MaximumSearchSeconds)
                {
                    ClearInvestigation();
                    UpdateDecisionState();
                }

                break;
            case MutantState.ChaseLastKnownPosition:
                if (HasReachedDestination())
                {
                    StopMovement();
                }

                if (_stateElapsedSeconds >= _definition.MaximumSearchSeconds)
                {
                    _hasConfirmedTargetMemory = false;
                    _hasLastKnownTargetPosition = false;
                    _timeSinceLastSeen = double.PositiveInfinity;
                    UpdateDecisionState();
                }

                break;
            case MutantState.Attack:
                UpdateAttack();
                break;
        }
    }

    private void UpdatePatrol(double delta)
    {
        if (_patrolPoints.Length == 0)
        {
            return;
        }

        if (!_isWaitingAtPatrolPoint && !HasReachedDestination())
        {
            return;
        }

        if (!_isWaitingAtPatrolPoint)
        {
            _isWaitingAtPatrolPoint = true;
            _patrolWaitElapsedSeconds = 0.0;
            StopMovement();
        }

        _patrolWaitElapsedSeconds += delta;
        if (_patrolWaitElapsedSeconds < _definition.PatrolWaitSeconds)
        {
            return;
        }

        _isWaitingAtPatrolPoint = false;
        _patrolPointIndex = (_patrolPointIndex + 1) % _patrolPoints.Length;
        SetNavigationDestination(_patrolPoints[_patrolPointIndex]);
    }

    private void UpdateChaseDestination(double delta)
    {
        if (_state != MutantState.Chase || !IsTargetAliveAndValid())
        {
            return;
        }

        PeriodicCatchUpResult refresh = _chaseRefreshTimer!.Advance(delta);
        if (refresh.DueTicks > 0)
        {
            SetNavigationDestination(_target!.GlobalPosition);
        }
    }

    private void UpdateAttack()
    {
        StopMovement();
        if (!IsTargetAliveAndValid())
        {
            UpdateDecisionState();
            return;
        }

        FaceDirection(_target!.GlobalPosition - GlobalPosition);
        if (_isAttackPending ||
            Time.GetTicksMsec() / 1000.0 < _nextAttackAllowedAtSeconds)
        {
            return;
        }

        _isAttackPending = true;
        _visualPivot.Scale = Vector3.One * 1.08f;
        _attackWindupTimer.Start(0.18);
    }

    private void OnAttackWindupTimeout()
    {
        _isAttackPending = false;
        _visualPivot.Scale = Vector3.One;
        if (_state != MutantState.Attack ||
            _isTerminal ||
            Health.IsDead ||
            !IsTargetAliveAndValid() ||
            !IsTargetInsideAttackRange() ||
            !HasUnblockedLineToTarget())
        {
            UpdateDecisionState();
            return;
        }

        DamageInfo damage = new(
            _definition.AttackDamage,
            this,
            $"{_definition.DisplayName} melee");
        HealthChangeResult result = _targetHealth!.ApplyDamage(damage);
        _nextAttackAllowedAtSeconds =
            (Time.GetTicksMsec() / 1000.0) +
            _definition.AttackCooldownSeconds;
        if (result.Changed)
        {
            SafeEventPublisher.Publish(
                MeleeAttackApplied,
                damage,
                result,
                $"{nameof(MutantController3D)}.{nameof(MeleeAttackApplied)}");
        }
    }

    private void UpdateMovement(float delta)
    {
        Vector3 desiredVelocity = Vector3.Zero;
        if (ShouldNavigate() && _hasNavigationDestination)
        {
            Vector3 nextPosition = _navigationDestination;
            if (_navigationReady && !_navigationAgent.IsNavigationFinished())
            {
                nextPosition = _navigationAgent.GetNextPathPosition();
            }

            Vector3 direction = Flatten(nextPosition - GlobalPosition);
            if (direction.LengthSquared() > MinimumDirectionSquared)
            {
                desiredVelocity =
                    direction.Normalized() * Scaled(_definition.MoveSpeed);
            }
        }

        Vector3 horizontal = new(Velocity.X, 0.0f, Velocity.Z);
        horizontal = horizontal.MoveToward(
            desiredVelocity,
            Scaled(_definition.Acceleration) * delta);
        float vertical = IsOnFloor()
            ? 0.0f
            : Mathf.Max(Velocity.Y - (Gravity * delta), -45.0f);
        Velocity = new Vector3(horizontal.X, vertical, horizontal.Z);
        if (desiredVelocity.LengthSquared() > MinimumDirectionSquared)
        {
            FaceDirection(desiredVelocity);
        }

        MoveAndSlide();
    }

    private bool ShouldNavigate()
    {
        return _state == MutantState.Patrol && !_isWaitingAtPatrolPoint ||
               _state == MutantState.Investigate ||
               _state == MutantState.Chase ||
               _state == MutantState.ChaseLastKnownPosition;
    }

    private void TransitionTo(MutantState nextState)
    {
        if (_state == nextState)
        {
            return;
        }

        MutantState previous = _state;
        CancelPendingAttack();
        _state = nextState;
        _stateElapsedSeconds = 0.0;
        _isWaitingAtPatrolPoint = false;
        switch (nextState)
        {
            case MutantState.Patrol:
                if (_patrolPoints.Length > 0)
                {
                    SetNavigationDestination(_patrolPoints[_patrolPointIndex]);
                }

                break;
            case MutantState.Investigate:
                SetNavigationDestination(_investigationPosition);
                break;
            case MutantState.Chase:
                if (IsTargetAliveAndValid())
                {
                    SetNavigationDestination(_target!.GlobalPosition);
                }

                break;
            case MutantState.ChaseLastKnownPosition:
                SetNavigationDestination(_lastKnownTargetPosition);
                break;
            case MutantState.Attack:
                StopMovement();
                break;
            case MutantState.Idle:
            case MutantState.Dead:
                StopMovement();
                break;
        }

        RefreshStateDisplay();
        SafeEventPublisher.Publish(
            StateChanged,
            previous,
            nextState,
            $"{nameof(MutantController3D)}.{nameof(StateChanged)}");
    }

    private void SetNavigationDestination(Vector3 destination)
    {
        destination.Y = GlobalPosition.Y;
        if (_hasNavigationDestination &&
            _navigationDestination.DistanceSquaredTo(destination) <=
                NavigationDestinationThresholdSquared)
        {
            return;
        }

        _navigationDestination = destination;
        _hasNavigationDestination = true;
        if (_navigationReady)
        {
            _navigationAgent.TargetPosition = destination;
        }
    }

    private void EnsureNavigationReady()
    {
        if (_navigationReady)
        {
            return;
        }

        Rid navigationMap = _navigationAgent.GetNavigationMap();
        if (!navigationMap.IsValid ||
            NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            return;
        }

        _navigationReady = true;
        if (_hasNavigationDestination)
        {
            _navigationAgent.TargetPosition = _navigationDestination;
        }
    }

    private bool HasReachedDestination()
    {
        if (!_hasNavigationDestination)
        {
            return true;
        }

        float distance = _navigationAgent.TargetDesiredDistance;
        return Flatten(GlobalPosition - _navigationDestination).LengthSquared() <=
               distance * distance ||
               (_navigationReady && _navigationAgent.IsNavigationFinished());
    }

    private bool IsTargetInsideSightCone()
    {
        Vector3 toTarget = Flatten(_target!.GlobalPosition - GlobalPosition);
        float distanceSquared = toTarget.LengthSquared();
        float visibilityMultiplier = _visibilityTarget!.State.FinalMultiplier;
        if (!float.IsFinite(visibilityMultiplier) || visibilityMultiplier <= 0.0f)
        {
            throw new InvalidOperationException(
                "Mutant3D received an invalid target visibility multiplier.");
        }

        float sightRange = Scaled(_definition.SightRange) * visibilityMultiplier;
        if (distanceSquared > sightRange * sightRange)
        {
            return false;
        }

        if (distanceSquared <= MinimumDirectionSquared ||
            _definition.FieldOfViewDegrees >= 359.9f)
        {
            return true;
        }

        Vector3 direction = toTarget / Mathf.Sqrt(distanceSquared);
        float halfFov = Mathf.DegToRad(_definition.FieldOfViewDegrees * 0.5f);
        return _facingDirection.Dot(direction) >= Mathf.Cos(halfFov);
    }

    private bool HasUnblockedLineToTarget()
    {
        if (!IsTargetAliveAndValid())
        {
            return false;
        }

        Vector3 rayEnd = _target!.GlobalPosition + (Vector3.Up * 0.8f);
        PhysicsRayQueryParameters3D query =
            PhysicsRayQueryParameters3D.Create(
                _sightOrigin.GlobalPosition,
                rayEnd,
                SightCollisionMask,
                _sightRayExclusions);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.HitFromInside = true;
        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    private bool IsTargetInsideAttackRange()
    {
        if (!IsTargetAliveAndValid())
        {
            return false;
        }

        float range = Scaled(_definition.AttackRange);
        return Flatten(_target!.GlobalPosition - GlobalPosition).LengthSquared() <=
               range * range;
    }

    private bool HasRecentConfirmedTargetMemory()
    {
        return _hasConfirmedTargetMemory &&
               _hasLastKnownTargetPosition &&
               _timeSinceLastSeen < _definition.LostTargetGraceSeconds;
    }

    private bool IsTargetAliveAndValid()
    {
        return IsTargetingEnabled &&
               _target is not null &&
               GodotObject.IsInstanceValid(_target) &&
               _target.IsInsideTree() &&
               _targetHealth is not null &&
               _targetHealth.IsAlive;
    }

    private bool IsNoiseFromTarget(Node source)
    {
        return _target is not null &&
               GodotObject.IsInstanceValid(source) &&
               (ReferenceEquals(source, _target) ||
                _target.IsAncestorOf(source));
    }

    private float CalculateNoiseScore(PerceivedNoise3D noise)
    {
        float kindWeight = noise.Occurrence.Noise.Kind switch
        {
            NoiseKind.Gunshot => 3.0f,
            NoiseKind.Interaction => 2.0f,
            NoiseKind.Footstep => 1.0f,
            _ => throw new InvalidOperationException("Unknown noise kind.")
        };
        return (kindWeight * noise.PerceivedIntensity) /
               (1.0f + noise.Distance);
    }

    private void ClearInvestigation()
    {
        _pendingNoise = null;
        _pendingNoiseScore = 0.0f;
        _hasInvestigationTarget = false;
    }

    private void CancelPendingAttack()
    {
        _isAttackPending = false;
        if (GodotObject.IsInstanceValid(_attackWindupTimer))
        {
            _attackWindupTimer.Stop();
        }

        if (GodotObject.IsInstanceValid(_visualPivot))
        {
            _visualPivot.Scale = Vector3.One;
        }
    }

    private void StopMovement()
    {
        Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
    }

    private void FaceDirection(Vector3 direction)
    {
        Vector3 horizontal = Flatten(direction);
        if (horizontal.LengthSquared() <= MinimumDirectionSquared)
        {
            return;
        }

        _facingDirection = horizontal.Normalized();
        Vector3 rotation = _visualPivot.Rotation;
        rotation.Y = MathF.Atan2(-_facingDirection.X, -_facingDirection.Z);
        _visualPivot.Rotation = rotation;
    }

    private Vector3[] CreatePatrolPoints(Vector3[] offsets)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        Vector3[] points = new Vector3[offsets.Length];
        for (int index = 0; index < offsets.Length; index++)
        {
            if (!IsFinite(offsets[index]))
            {
                throw new InvalidOperationException(
                    "Mutant3D patrol offsets must be finite.");
            }

            points[index] = GlobalPosition + offsets[index];
        }

        return points;
    }

    private void OnHealthChanged(HealthChangeResult result)
    {
        RefreshHealthDisplay();
    }

    private void OnDamaged(DamageInfo damage, HealthChangeResult result)
    {
        if (result.CausedDeath ||
            !IsTargetAliveAndValid() ||
            damage.Source is not Node source ||
            !IsNoiseFromTarget(source))
        {
            return;
        }

        _lastKnownTargetPosition = _target!.GlobalPosition;
        _hasConfirmedTargetMemory = true;
        _hasLastKnownTargetPosition = true;
        _timeSinceLastSeen = 0.0;
        TransitionTo(MutantState.Chase);
    }

    private void OnDied(DamageInfo damage, HealthChangeResult result)
    {
        _isTargetingEnabled = false;
        _canCurrentlySeeTarget = false;
        ClearInvestigation();
        CancelPendingAttack();
        CollisionLayer = 0;
        TransitionTo(MutantState.Dead);
        SetPhysicsProcess(false);
    }

    private void OnTargetDied(DamageInfo damage, HealthChangeResult result)
    {
        DisableTargeting();
    }

    private void OnTargetTreeExiting()
    {
        DisableTargeting();
    }

    private void PublishNoiseAccepted(PerceivedNoise3D noise)
    {
        SafeEventPublisher.Publish(
            NoiseAccepted,
            noise,
            $"{nameof(MutantController3D)}.{nameof(NoiseAccepted)}");
    }

    private void RefreshHealthDisplay()
    {
        _healthLabel.Visible = EnableDebugHealthLabel;
        _healthLabel.Text = Health.IsDead
            ? "MUTANT DEAD"
            : $"{_definition.DisplayName.ToUpperInvariant()} " +
              $"{Health.CurrentHealth} / {Health.MaxHealth}";
    }

    private void RefreshStateDisplay()
    {
        _stateLabel.Visible = EnableDebugStateLabel;
        _stateLabel.Text = _state.ToString().ToUpperInvariant();
    }

    private float Scaled(float authoredDistance)
    {
        float value = authoredDistance * WorldDistanceScale;
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new InvalidOperationException(
                "Scaled mutant distance must be finite and positive.");
        }

        return value;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.Y = 0.0f;
        return value;
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }

    private static bool IsFiniteHorizontalDirection(Vector3 value)
    {
        return IsFinite(value) &&
               Flatten(value).LengthSquared() > MinimumDirectionSquared;
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(MutantController3D)} on '{Name}' requires '{path}'.");
    }
}
