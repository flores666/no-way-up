using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Noise;

namespace LineZero.World3D.Noise;

public sealed partial class PlayerFootstepNoiseEmitter3D : Node
{
    private const int MaximumFootstepsPerPhysicsUpdate = 4;

    private readonly FootstepCadenceModel _cadence = new();

    private PlayerController3D? _player;
    private HealthModel? _health;
    private NoiseSystem3D? _noiseSystem;
    private Vector3 _lastPosition;
    private ulong _nextDescriptionSequence = 1;
    private bool _isBound;
    private bool _isEmissionEnabled = true;

    [Export(PropertyHint.Range, "0.1,20.0,0.05")]
    public float WalkStepDistance { get; set; } = 2.2f;

    [Export(PropertyHint.Range, "0.01,5.0,0.01")]
    public float WalkIntensity { get; set; } = 0.8f;

    [Export(PropertyHint.Range, "0.1,20.0,0.05")]
    public float CrouchStepDistance { get; set; } = 3.4f;

    [Export(PropertyHint.Range, "0.01,5.0,0.01")]
    public float CrouchIntensity { get; set; } = 0.45f;

    [Export(PropertyHint.Range, "0.1,20.0,0.05")]
    public float SprintStepDistance { get; set; } = 1.6f;

    [Export(PropertyHint.Range, "0.01,5.0,0.01")]
    public float SprintIntensity { get; set; } = 1.8f;

    [Export(PropertyHint.Range, "0.1,20.0,0.05")]
    public float CrawlStepDistance { get; set; } = 4.5f;

    [Export(PropertyHint.Range, "0.01,5.0,0.01")]
    public float CrawlIntensity { get; set; } = 0.2f;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0")]
    public float TeleportResetDistance { get; set; } = 64.0f;

    public long PendingStepDebt => _cadence.PendingSteps;

    public event Action<NoiseOccurrence3D>? FootstepEmitted;

    public override void _Ready()
    {
        ValidateTuning();
        SetPhysicsProcess(false);
    }

    public override void _ExitTree()
    {
        _player = null;
        _health = null;
        _noiseSystem = null;
        _isBound = false;
        _cadence.Reset();
        FootstepEmitted = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        EnsureBound();
        PlayerController3D player = _player!;
        Vector3 currentPosition = player.GlobalPosition;
        Vector3 previousPosition = _lastPosition;
        _lastPosition = currentPosition;

        if (_health!.IsDead)
        {
            _cadence.Reset();
            return;
        }

        if (!_isEmissionEnabled || !player.IsGameplayInputEnabled)
        {
            // Modal input pauses emission without erasing accumulated distance
            // debt. Any bounded catch-up resumes after gameplay is re-enabled.
            return;
        }

        if (!IsFinite(currentPosition) ||
            !IsFinite(previousPosition) ||
            !double.IsFinite(delta) ||
            delta <= 0.0)
        {
            _cadence.Reset();
            return;
        }

        int remainingEmissionBudget = MaximumFootstepsPerPhysicsUpdate;
        EmitPendingSteps(player, ref remainingEmissionBudget);

        Vector3 displacement = currentPosition - previousPosition;
        displacement.Y = 0.0f;
        float travelledDistance = displacement.Length();
        if (!float.IsFinite(travelledDistance) ||
            travelledDistance > TeleportResetDistance)
        {
            _cadence.Reset();
            return;
        }

        if (travelledDistance <= 0.0001f)
        {
            return;
        }

        GetTuning(
            player.CurrentMovementMode,
            out float stepDistance,
            out float intensity);
        _cadence.Advance(travelledDistance, stepDistance, intensity);
        EmitPendingSteps(player, ref remainingEmissionBudget);
    }

    public void Bind(
        PlayerController3D player,
        HealthModel health,
        NoiseSystem3D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_isBound)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter3D)} on '{Name}' is already bound.");
        }

        if (!GodotObject.IsInstanceValid(player) ||
            !player.IsInsideTree() ||
            !ReferenceEquals(GetParent(), player) ||
            !GodotObject.IsInstanceValid(noiseSystem) ||
            !noiseSystem.IsInsideTree())
        {
            throw new ArgumentException(
                "3D footstep dependencies must be active scene nodes.");
        }

        _player = player;
        _health = health;
        _noiseSystem = noiseSystem;
        _lastPosition = player.GlobalPosition;
        _cadence.Reset();
        _isBound = true;
        SetPhysicsProcess(true);
    }

    public void SetEmissionEnabled(bool enabled)
    {
        if (_isEmissionEnabled == enabled)
        {
            return;
        }

        _isEmissionEnabled = enabled;
        if (_player is not null && GodotObject.IsInstanceValid(_player))
        {
            _lastPosition = _player.GlobalPosition;
        }
    }

    public void StopAndClear()
    {
        _isEmissionEnabled = false;
        _cadence.Reset();
        if (_player is not null && GodotObject.IsInstanceValid(_player))
        {
            _lastPosition = _player.GlobalPosition;
        }
    }

    private void EmitPendingSteps(
        PlayerController3D player,
        ref int remainingEmissionBudget)
    {
        while (remainingEmissionBudget > 0 &&
               _cadence.TryTakePendingStep(out float intensity))
        {
            remainingEmissionBudget--;
            if (_nextDescriptionSequence == ulong.MaxValue)
            {
                _nextDescriptionSequence = 1;
            }

            NoiseOccurrence3D occurrence = _noiseSystem!.EmitNoise(
                player,
                NoiseKind.Footstep,
                intensity,
                player.GlobalPosition,
                player,
                $"Player footstep {_nextDescriptionSequence}");
            _nextDescriptionSequence++;
            SafeEventPublisher.Publish(
                FootstepEmitted,
                occurrence,
                $"{nameof(PlayerFootstepNoiseEmitter3D)}.{nameof(FootstepEmitted)}");
        }
    }

    private void GetTuning(
        MovementMode movementMode,
        out float stepDistance,
        out float intensity)
    {
        switch (movementMode)
        {
            case MovementMode.Walk:
                stepDistance = WalkStepDistance;
                intensity = WalkIntensity;
                break;
            case MovementMode.Crouch:
                stepDistance = CrouchStepDistance;
                intensity = CrouchIntensity;
                break;
            case MovementMode.Sprint:
                stepDistance = SprintStepDistance;
                intensity = SprintIntensity;
                break;
            case MovementMode.Crawl:
                stepDistance = CrawlStepDistance;
                intensity = CrawlIntensity;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(movementMode));
        }
    }

    private void ValidateTuning()
    {
        ValidatePositiveFinite(WalkStepDistance, nameof(WalkStepDistance));
        ValidatePositiveFinite(WalkIntensity, nameof(WalkIntensity));
        ValidatePositiveFinite(CrouchStepDistance, nameof(CrouchStepDistance));
        ValidatePositiveFinite(CrouchIntensity, nameof(CrouchIntensity));
        ValidatePositiveFinite(SprintStepDistance, nameof(SprintStepDistance));
        ValidatePositiveFinite(SprintIntensity, nameof(SprintIntensity));
        ValidatePositiveFinite(CrawlStepDistance, nameof(CrawlStepDistance));
        ValidatePositiveFinite(CrawlIntensity, nameof(CrawlIntensity));
        ValidatePositiveFinite(TeleportResetDistance, nameof(TeleportResetDistance));
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }

    private static void ValidatePositiveFinite(float value, string propertyName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new InvalidOperationException(
                $"Footstep property '{propertyName}' must be finite and positive.");
        }
    }

    private void EnsureBound()
    {
        if (!_isBound)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerFootstepNoiseEmitter3D)} on '{Name}' is not bound.");
        }
    }
}
