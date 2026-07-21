using System;
using System.Collections.Generic;
using Godot;
using LineZero.Data;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Perception;

namespace LineZero.World2D.Perception;

public sealed partial class PlayerVisibilityController2D : Node, IVisibilityTarget
{
    public const float DefaultAmbientLightMultiplier = 1.0f;
    public const string DefaultAmbientZoneName = "Normal area";

    private const float WalkVisibilityMultiplier = 1.0f;
    private const float CrouchVisibilityMultiplier = 0.65f;
    private const float SprintVisibilityMultiplier = 1.15f;
    private const float HiddenThreshold = 0.55f;
    private const float DimThreshold = 0.85f;
    private const float ExposedThreshold = 1.30f;

    private readonly List<LightExposureZone2D> _activeZones = new();
    private IMovementModeSource? _movementSource;
    private PlayerMovementSettings? _movementSettings;
    private FlashlightModel? _flashlight;
    private HealthModel? _health;
    private LightExposureZone2D? _effectiveZone;
    private VisibilityState _state;
    private bool _isInitialized;

    [Export(PropertyHint.Range, "1.0,5.0,0.01,or_greater")]
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
        if (!float.IsFinite(FlashlightOnMultiplier) ||
            FlashlightOnMultiplier <= 1.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerVisibilityController2D)} on '{Name}' requires a finite " +
                "flashlight multiplier greater than one.");
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
        PlayerMovementSettings movementSettings,
        FlashlightModel flashlight,
        HealthModel health)
    {
        ArgumentNullException.ThrowIfNull(movementSource);
        ArgumentNullException.ThrowIfNull(movementSettings);
        ArgumentNullException.ThrowIfNull(flashlight);
        ArgumentNullException.ThrowIfNull(health);
        if (_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerVisibilityController2D)} on '{Name}' is already initialized.");
        }

        movementSettings.Validate();
        _movementSource = movementSource;
        _movementSettings = movementSettings;
        _flashlight = flashlight;
        _health = health;
        _movementSource.MovementModeChanged += OnMovementModeChanged;
        _flashlight.PowerStateChanged += OnFlashlightPowerStateChanged;
        _health.Died += OnActorDied;
        _isInitialized = true;
        _state = CalculateState();
    }

    public void EnterZone(LightExposureZone2D zone)
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

    public void ExitZone(LightExposureZone2D zone)
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

    private void OnMovementModeChanged(MovementMode previousMode, MovementMode currentMode)
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
        bool effectiveValueChanged = !Mathf.IsEqualApprox(
            _state.FinalMultiplier,
            nextState.FinalMultiplier);
        bool categoryChanged = _state.Category != nextState.Category;
        bool lifeStateChanged = _state.IsActorAlive != nextState.IsActorAlive;
        _state = nextState;
        if (effectiveValueChanged || categoryChanged || lifeStateChanged)
        {
            VisibilityChanged?.Invoke(nextState);
        }
    }

    private VisibilityState CalculateState()
    {
        IMovementModeSource movementSource = _movementSource
            ?? throw new InvalidOperationException("Movement source is not bound.");
        PlayerMovementSettings movementSettings = _movementSettings
            ?? throw new InvalidOperationException("Movement settings are not bound.");
        FlashlightModel flashlight = _flashlight
            ?? throw new InvalidOperationException("Flashlight model is not bound.");
        HealthModel health = _health
            ?? throw new InvalidOperationException("Health model is not bound.");

        RemoveInvalidZones();
        _effectiveZone = SelectEffectiveZone();
        float postureMultiplier = movementSource.CurrentMovementMode switch
        {
            MovementMode.Walk => WalkVisibilityMultiplier,
            MovementMode.Crouch => CrouchVisibilityMultiplier,
            MovementMode.Sprint => SprintVisibilityMultiplier,
            MovementMode.Crawl => movementSettings.CrawlVisibilityMultiplier,
            _ => throw new InvalidOperationException("Unknown player movement mode.")
        };
        float ambientMultiplier = _effectiveZone?.VisibilityMultiplier ??
            DefaultAmbientLightMultiplier;
        float flashlightMultiplier = flashlight.IsOn
            ? FlashlightOnMultiplier
            : 1.0f;
        float finalMultiplier = postureMultiplier * ambientMultiplier * flashlightMultiplier;
        if (!float.IsFinite(finalMultiplier) || finalMultiplier <= 0.0f)
        {
            throw new InvalidOperationException(
                "Calculated player visibility multiplier must be finite and positive.");
        }

        VisibilityCategory category = finalMultiplier switch
        {
            < HiddenThreshold => VisibilityCategory.Hidden,
            < DimThreshold => VisibilityCategory.Dim,
            < ExposedThreshold => VisibilityCategory.Visible,
            _ => VisibilityCategory.Exposed
        };
        string zoneName = _effectiveZone?.DisplayName ?? DefaultAmbientZoneName;
        return new VisibilityState(
            postureMultiplier,
            ambientMultiplier,
            flashlightMultiplier,
            finalMultiplier,
            category,
            health.IsAlive,
            zoneName);
    }

    private LightExposureZone2D? SelectEffectiveZone()
    {
        LightExposureZone2D? selected = null;
        for (int index = 0; index < _activeZones.Count; index++)
        {
            LightExposureZone2D candidate = _activeZones[index];
            if (selected is null || IsPreferred(candidate, selected))
            {
                selected = candidate;
            }
        }

        return selected;
    }

    private static bool IsPreferred(
        LightExposureZone2D candidate,
        LightExposureZone2D current)
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
            LightExposureZone2D zone = _activeZones[index];
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
        _movementSettings = null;
        _flashlight = null;
        _health = null;
        _isInitialized = false;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerVisibilityController2D)} on '{Name}' is not initialized.");
        }
    }
}
