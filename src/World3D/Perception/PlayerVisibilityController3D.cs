using System;
using System.Collections.Generic;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Perception;

namespace LineZero.World3D.Perception;

public sealed partial class PlayerVisibilityController3D : Node,
    IVisibilityStateSource
{
    private readonly List<LightExposureZone3D> _activeZones = new();

    private IMovementModeSource? _movementSource;
    private FlashlightModel? _flashlight;
    private HealthModel? _health;
    private LightExposureZone3D? _effectiveZone;
    private VisibilityState _state;
    private bool _isInitialized;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float CrawlVisibilityMultiplier { get; set; } = 0.40f;

    [Export(PropertyHint.Range, "1.0,5.0,0.01")]
    public float FlashlightOnMultiplier { get; set; } = 1.45f;

    public float VisibilityMultiplier => State.FinalMultiplier;

    public VisibilityState State
    {
        get
        {
            EnsureInitialized();
            return _state;
        }
    }

    public event Action<VisibilityState>? VisibilityChanged;

    public override void _Ready()
    {
        if (!float.IsFinite(CrawlVisibilityMultiplier) ||
            CrawlVisibilityMultiplier <= 0.0f ||
            CrawlVisibilityMultiplier >= 0.65f ||
            !float.IsFinite(FlashlightOnMultiplier) ||
            FlashlightOnMultiplier <= 1.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerVisibilityController3D)} on '{Name}' has invalid tuning.");
        }
    }

    public override void _ExitTree()
    {
        Unbind();
        _activeZones.Clear();
        _effectiveZone = null;
        VisibilityChanged = null;
    }

    public void Initialize(
        IMovementModeSource movementSource,
        FlashlightModel flashlight,
        HealthModel health)
    {
        ArgumentNullException.ThrowIfNull(movementSource);
        ArgumentNullException.ThrowIfNull(flashlight);
        ArgumentNullException.ThrowIfNull(health);
        if (_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerVisibilityController3D)} on '{Name}' is already initialized.");
        }

        _movementSource = movementSource;
        _flashlight = flashlight;
        _health = health;
        _movementSource.MovementModeChanged += OnMovementModeChanged;
        _flashlight.PowerStateChanged += OnFlashlightPowerStateChanged;
        _health.Died += OnActorDied;
        _isInitialized = true;
        _state = CalculateState();
    }

    public void EnterZone(LightExposureZone3D zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        EnsureInitialized();
        RemoveInvalidZones();
        if (_activeZones.Contains(zone))
        {
            return;
        }

        zone.ValidateConfiguration();
        _activeZones.Add(zone);
        RecalculateAndNotify();
    }

    public void ExitZone(LightExposureZone3D zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (!_isInitialized)
        {
            return;
        }

        bool removed = _activeZones.Remove(zone);
        RemoveInvalidZones();
        if (removed || ReferenceEquals(_effectiveZone, zone))
        {
            RecalculateAndNotify();
        }
    }

    private void OnMovementModeChanged(
        MovementMode previousMode,
        MovementMode currentMode)
    {
        RecalculateAndNotify();
    }

    private void OnFlashlightPowerStateChanged(bool isOn)
    {
        RecalculateAndNotify();
    }

    private void OnActorDied(DamageInfo damage, HealthChangeResult result)
    {
        RecalculateAndNotify();
    }

    private void RecalculateAndNotify()
    {
        EnsureInitialized();
        VisibilityState nextState = CalculateState();
        bool changed = !Mathf.IsEqualApprox(
                           _state.FinalMultiplier,
                           nextState.FinalMultiplier) ||
                       _state.Category != nextState.Category ||
                       _state.IsActorAlive != nextState.IsActorAlive ||
                       !string.Equals(
                           _state.AmbientZoneName,
                           nextState.AmbientZoneName,
                           StringComparison.Ordinal);
        _state = nextState;
        if (changed)
        {
            SafeEventPublisher.Publish(
                VisibilityChanged,
                nextState,
                $"{nameof(PlayerVisibilityController3D)}.{nameof(VisibilityChanged)}");
        }
    }

    private VisibilityState CalculateState()
    {
        IMovementModeSource movementSource = _movementSource
            ?? throw new InvalidOperationException("3D movement source is missing.");
        FlashlightModel flashlight = _flashlight
            ?? throw new InvalidOperationException("3D flashlight model is missing.");
        HealthModel health = _health
            ?? throw new InvalidOperationException("3D health model is missing.");
        RemoveInvalidZones();
        _effectiveZone = SelectEffectiveZone();
        return VisibilityRules.Calculate(
            movementSource.CurrentMovementMode,
            CrawlVisibilityMultiplier,
            _effectiveZone?.VisibilityMultiplier ??
                VisibilityRules.DefaultAmbientLightMultiplier,
            flashlight.IsOn,
            FlashlightOnMultiplier,
            health.IsAlive,
            _effectiveZone?.DisplayName ?? VisibilityRules.DefaultAmbientZoneName);
    }

    private LightExposureZone3D? SelectEffectiveZone()
    {
        LightExposureZone3D? selected = null;
        for (int index = 0; index < _activeZones.Count; index++)
        {
            LightExposureZone3D candidate = _activeZones[index];
            if (selected is null || IsPreferred(candidate, selected))
            {
                selected = candidate;
            }
        }

        return selected;
    }

    private static bool IsPreferred(
        LightExposureZone3D candidate,
        LightExposureZone3D current)
    {
        if (candidate.ZonePriority != current.ZonePriority)
        {
            return candidate.ZonePriority > current.ZonePriority;
        }

        int nameComparison = string.Compare(
            candidate.DisplayName,
            current.DisplayName,
            StringComparison.Ordinal);
        if (nameComparison != 0)
        {
            return nameComparison < 0;
        }

        return string.Compare(
            candidate.GetPath().ToString(),
            current.GetPath().ToString(),
            StringComparison.Ordinal) < 0;
    }

    private void RemoveInvalidZones()
    {
        for (int index = _activeZones.Count - 1; index >= 0; index--)
        {
            LightExposureZone3D zone = _activeZones[index];
            if (!GodotObject.IsInstanceValid(zone) || !zone.IsInsideTree())
            {
                _activeZones.RemoveAt(index);
            }
        }
    }

    private void Unbind()
    {
        if (_movementSource is not null)
        {
            _movementSource.MovementModeChanged -= OnMovementModeChanged;
        }

        if (_flashlight is not null)
        {
            _flashlight.PowerStateChanged -= OnFlashlightPowerStateChanged;
        }

        if (_health is not null)
        {
            _health.Died -= OnActorDied;
        }

        _movementSource = null;
        _flashlight = null;
        _health = null;
        _isInitialized = false;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerVisibilityController3D)} on '{Name}' is not initialized.");
        }
    }
}
