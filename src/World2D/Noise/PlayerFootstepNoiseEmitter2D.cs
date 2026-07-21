using System;
using Godot;
using LineZero.Data;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Noise;

namespace LineZero.World2D.Noise;

public sealed partial class PlayerFootstepNoiseEmitter2D : Node2D, INoiseEmitter2D
{
    private const float MaximumExpectedTravelMultiplier = 3.0f;
    private const double MaximumTrackablePhysicsDeltaSeconds = 1.0;
    private const int MaximumFootstepEventsPerPhysicsUpdate = 3;
    private const int MaximumPendingFootstepDebt = 24;
    private const double CycleCompletionTolerance = 0.000000001;

    private readonly PendingFootstep[] _pendingFootsteps =
        new PendingFootstep[MaximumPendingFootstepDebt];

    private PlayerController2D? _player;
    private HealthModel? _health;
    private PlayerMovementSettings? _movementSettings;
    private NoiseSystem2D? _noiseSystem;
    private Vector2 _lastPosition;
    private double _stepCycleProgress;
    private double _stepCycleWeightedIntensity;
    private double _stepCyclePhysicalDistance;
    private int _pendingFootstepHead;
    private int _pendingFootstepCount;
    private ulong _nextFootstepDescriptionSequence = 1;
    private bool _isInitialized;
    private bool _isEmissionEnabled = true;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0,or_greater")]
    public float WalkStepDistance { get; set; } = 110.0f;

    [Export(PropertyHint.Range, "0.01,5.0,0.01,or_greater")]
    public float WalkFootstepIntensity { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0,or_greater")]
    public float CrouchStepDistance { get; set; } = 132.0f;

    [Export(PropertyHint.Range, "0.01,5.0,0.01,or_greater")]
    public float CrouchFootstepIntensity { get; set; } = 0.45f;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0,or_greater")]
    public float SprintStepDistance { get; set; } = 90.0f;

    [Export(PropertyHint.Range, "0.01,5.0,0.01,or_greater")]
    public float SprintFootstepIntensity { get; set; } = 1.8f;

    public event Action<NoiseOccurrence2D>? FootstepEmitted;

    public override void _Ready()
    {
        ValidateBaseTuning();
    }

    public override void _ExitTree()
    {
        _noiseSystem = null;
        _player = null;
        _health = null;
        _movementSettings = null;
        _isInitialized = false;
        ResetFootstepTracking();
        FootstepEmitted = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        EnsureReadyForSimulation();
        PlayerController2D player = _player!;
        PlayerMovementSettings movementSettings = _movementSettings!;
        Vector2 currentPosition = player.GlobalPosition;
        Vector2 previousPosition = _lastPosition;
        _lastPosition = currentPosition;

        if (!_isEmissionEnabled ||
            _health!.IsDead ||
            !player.IsGameplayInputEnabled)
        {
            ResetFootstepTracking();
            return;
        }

        if (!IsFinitePosition(currentPosition) ||
            !IsFinitePosition(previousPosition) ||
            !double.IsFinite(delta) ||
            delta <= 0.0 ||
            delta > MaximumTrackablePhysicsDeltaSeconds)
        {
            ResetFootstepTracking();
            return;
        }

        float traveledDistance = currentPosition.DistanceTo(previousPosition);
        float maximumExpectedTravel =
            (movementSettings.SprintSpeed * (float)delta * MaximumExpectedTravelMultiplier) +
            movementSettings.MinimumActualMovementDistance;
        if (!float.IsFinite(traveledDistance) || traveledDistance > maximumExpectedTravel)
        {
            ResetFootstepTracking();
            return;
        }

        int remainingEmissionBudget = MaximumFootstepEventsPerPhysicsUpdate;
        EmitPendingFootsteps(ref remainingEmissionBudget, player);

        if (traveledDistance < movementSettings.MinimumActualMovementDistance)
        {
            return;
        }

        GetFootstepTuning(
            player.CurrentMovementMode,
            movementSettings,
            out float stepDistance,
            out float footstepIntensity);
        AccumulateSegment(
            traveledDistance,
            stepDistance,
            footstepIntensity,
            currentPosition);
        EmitPendingFootsteps(ref remainingEmissionBudget, player);
    }

    public void Initialize(
        PlayerController2D player,
        HealthModel health,
        PlayerMovementSettings movementSettings)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(movementSettings);
        if (_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' is already initialized.");
        }

        if (!GodotObject.IsInstanceValid(player) ||
            !player.IsInsideTree() ||
            !ReferenceEquals(GetParent(), player))
        {
            throw new ArgumentException(
                "The footstep emitter requires its active parent player.",
                nameof(player));
        }

        movementSettings.Validate();
        ValidateCrawlTuning(movementSettings);
        _player = player;
        _health = health;
        _movementSettings = movementSettings;
        _lastPosition = player.GlobalPosition;
        ResetFootstepTracking();
        _isInitialized = true;
    }

    public void BindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' already has a noise system.");
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

    public void SetEmissionEnabled(bool enabled)
    {
        if (_isEmissionEnabled == enabled)
        {
            return;
        }

        _isEmissionEnabled = enabled;
        ResetFootstepTracking();
        if (_player is not null && GodotObject.IsInstanceValid(_player))
        {
            _lastPosition = _player.GlobalPosition;
        }
    }

    private void AccumulateSegment(
        float traveledDistance,
        float stepDistance,
        float footstepIntensity,
        Vector2 completionPosition)
    {
        double remainingProgress = traveledDistance / stepDistance;
        while (remainingProgress > CycleCompletionTolerance)
        {
            double progressNeeded = 1.0 - _stepCycleProgress;
            double consumedProgress = Math.Min(remainingProgress, progressNeeded);
            _stepCycleProgress += consumedProgress;
            _stepCycleWeightedIntensity += consumedProgress * footstepIntensity;
            _stepCyclePhysicalDistance += consumedProgress * stepDistance;
            remainingProgress -= consumedProgress;

            if (_stepCycleProgress < 1.0 - CycleCompletionTolerance)
            {
                continue;
            }

            if (!double.IsFinite(_stepCyclePhysicalDistance) ||
                _stepCyclePhysicalDistance <= 0.0)
            {
                throw new InvalidOperationException(
                    "Accumulated footstep distance must be finite and positive.");
            }

            float completedIntensity = (float)_stepCycleWeightedIntensity;
            if (!float.IsFinite(completedIntensity) || completedIntensity <= 0.0f)
            {
                throw new InvalidOperationException(
                    "Accumulated footstep intensity must be finite and positive.");
            }

            completedIntensity = Math.Min(completedIntensity, SprintFootstepIntensity);
            EnqueuePendingFootstep(completedIntensity, completionPosition);
            ResetStepCycle();
        }
    }

    private void EnqueuePendingFootstep(float intensity, Vector2 position)
    {
        if (_pendingFootstepCount >= MaximumPendingFootstepDebt)
        {
            // A sustained severe stall cannot create an unbounded delayed burst.
            // Existing debt remains ordered; excess completed steps are safely clamped.
            return;
        }

        int tailIndex =
            (_pendingFootstepHead + _pendingFootstepCount) % MaximumPendingFootstepDebt;
        _pendingFootsteps[tailIndex] = new PendingFootstep(intensity, position);
        _pendingFootstepCount++;
    }

    private void EmitPendingFootsteps(
        ref int remainingEmissionBudget,
        PlayerController2D player)
    {
        while (remainingEmissionBudget > 0 && _pendingFootstepCount > 0)
        {
            PendingFootstep pending = _pendingFootsteps[_pendingFootstepHead];
            _pendingFootstepHead =
                (_pendingFootstepHead + 1) % MaximumPendingFootstepDebt;
            _pendingFootstepCount--;
            remainingEmissionBudget--;
            EmitFootstep(pending.Intensity, pending.Position, player);
        }

        if (_pendingFootstepCount == 0)
        {
            _pendingFootstepHead = 0;
        }
    }

    private void EmitFootstep(
        float intensity,
        Vector2 position,
        PlayerController2D player)
    {
        if (_nextFootstepDescriptionSequence == ulong.MaxValue)
        {
            _nextFootstepDescriptionSequence = 1;
        }

        string description = $"Player footstep {_nextFootstepDescriptionSequence}";
        _nextFootstepDescriptionSequence++;
        NoiseOccurrence2D occurrence = _noiseSystem!.EmitNoise(
            player,
            NoiseKind.Footstep,
            intensity,
            position,
            player,
            description);
        FootstepEmitted?.Invoke(occurrence);
    }

    private void ResetFootstepTracking()
    {
        ResetStepCycle();
        _pendingFootstepHead = 0;
        _pendingFootstepCount = 0;
    }

    private void ResetStepCycle()
    {
        _stepCycleProgress = 0.0;
        _stepCycleWeightedIntensity = 0.0;
        _stepCyclePhysicalDistance = 0.0;
    }

    private static bool IsFinitePosition(Vector2 position)
    {
        return float.IsFinite(position.X) && float.IsFinite(position.Y);
    }

    private void EnsureReadyForSimulation()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' is not initialized.");
        }

        if (_noiseSystem is null || !GodotObject.IsInstanceValid(_noiseSystem))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' has no bound noise system.");
        }
    }

    private void ValidateBaseTuning()
    {
        if (!float.IsFinite(CrouchStepDistance) || CrouchStepDistance <= 0.0f ||
            !float.IsFinite(WalkStepDistance) || WalkStepDistance <= 0.0f ||
            !float.IsFinite(SprintStepDistance) || SprintStepDistance <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' requires positive " +
                "finite step distances.");
        }

        if (!(SprintStepDistance < WalkStepDistance &&
              WalkStepDistance < CrouchStepDistance))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' requires " +
                "SprintDistance < WalkDistance < CrouchDistance.");
        }

        if (!float.IsFinite(CrouchFootstepIntensity) ||
            CrouchFootstepIntensity <= 0.0f ||
            !float.IsFinite(WalkFootstepIntensity) ||
            WalkFootstepIntensity <= 0.0f ||
            !float.IsFinite(SprintFootstepIntensity) ||
            SprintFootstepIntensity <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' requires positive " +
                "finite intensities.");
        }

        if (!(CrouchFootstepIntensity < WalkFootstepIntensity &&
              WalkFootstepIntensity < SprintFootstepIntensity))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' requires " +
                "CrouchIntensity < WalkIntensity < SprintIntensity.");
        }
    }

    private void ValidateCrawlTuning(PlayerMovementSettings movementSettings)
    {
        float crawlStepDistance =
            WalkStepDistance * movementSettings.CrawlStepDistanceMultiplier;
        float crawlIntensity =
            WalkFootstepIntensity * movementSettings.CrawlFootstepIntensityMultiplier;
        if (!float.IsFinite(crawlStepDistance) ||
            crawlStepDistance <= CrouchStepDistance)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' requires crawl steps " +
                "to have the longest distance threshold.");
        }

        if (!float.IsFinite(crawlIntensity) ||
            crawlIntensity <= 0.0f ||
            crawlIntensity >= CrouchFootstepIntensity)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter2D)} on '{Name}' requires crawl footsteps " +
                "to have the lowest positive intensity.");
        }
    }

    private void GetFootstepTuning(
        MovementMode movementMode,
        PlayerMovementSettings movementSettings,
        out float stepDistance,
        out float footstepIntensity)
    {
        switch (movementMode)
        {
            case MovementMode.Walk:
                stepDistance = WalkStepDistance;
                footstepIntensity = WalkFootstepIntensity;
                return;
            case MovementMode.Crouch:
                stepDistance = CrouchStepDistance;
                footstepIntensity = CrouchFootstepIntensity;
                return;
            case MovementMode.Sprint:
                stepDistance = SprintStepDistance;
                footstepIntensity = SprintFootstepIntensity;
                return;
            case MovementMode.Crawl:
                stepDistance =
                    WalkStepDistance * movementSettings.CrawlStepDistanceMultiplier;
                footstepIntensity =
                    WalkFootstepIntensity *
                    movementSettings.CrawlFootstepIntensityMultiplier;
                return;
            default:
                throw new InvalidOperationException("Unknown player movement mode.");
        }
    }

    private readonly struct PendingFootstep
    {
        public PendingFootstep(float intensity, Vector2 position)
        {
            Intensity = intensity;
            Position = position;
        }

        public float Intensity { get; }

        public Vector2 Position { get; }
    }
}
