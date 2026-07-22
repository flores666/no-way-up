using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Flashlight;
using LineZero.World3D;

namespace LineZero.World3D.Flashlight;

public sealed partial class PlayerFlashlightController3D : Node3D
{
    private FlashlightModel? _model;
    private SpotLight3D _spotLight = null!;
    private bool _isFlashlightInputEnabled = true;
    private bool _isActorAlive = true;
    private bool _requiresInputRelease;

    [Export]
    public FlashlightDefinition? FlashlightDefinition { get; set; }

    [Export]
    public bool StartOn { get; set; } = true;

    public FlashlightModel Model => _model
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerFlashlightController3D)} on '{Name}' has no model.");

    public bool IsFlashlightInputEnabled =>
        _isFlashlightInputEnabled && _isActorAlive;

    public event Action? BatteryReplacementRequested;

    public override void _Ready()
    {
        FlashlightDefinition definition = FlashlightDefinition
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerFlashlightController3D)} on '{Name}' requires a definition.");
        definition.Validate();
        _spotLight = GetNodeOrNull<SpotLight3D>("%FlashlightSpotLight3D")
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerFlashlightController3D)} on '{Name}' requires a SpotLight3D.");
        if (!float.IsFinite(_spotLight.SpotRange) || _spotLight.SpotRange <= 0.0f ||
            !float.IsFinite(_spotLight.SpotAngle) || _spotLight.SpotAngle <= 0.0f)
        {
            throw new InvalidOperationException(
                "FlashlightSpotLight3D requires positive finite range and angle.");
        }

        if (_spotLight.LightCullMask != RenderLayers3D.World)
        {
            throw new InvalidOperationException(
                "FlashlightSpotLight3D must illuminate only the world render layer.");
        }

        if (!float.IsFinite(_spotLight.ShadowOpacity) ||
            _spotLight.ShadowOpacity <= 0.0f ||
            _spotLight.ShadowOpacity >= 0.8f)
        {
            throw new InvalidOperationException(
                "FlashlightSpotLight3D requires controlled shadow opacity below 0.8.");
        }

        if (!Position.IsFinite() || Position.Z >= -0.6f ||
            !Rotation.IsFinite() || RotationDegrees.X >= -20.0f)
        {
            throw new InvalidOperationException(
                "Flashlight origin must be finite, ahead of the player, and angled toward the floor.");
        }

        _model = new FlashlightModel(definition, StartOn);
        _model.Changed += OnModelChanged;
        ApplyPresentation();
    }

    public override void _ExitTree()
    {
        if (_model is not null)
        {
            _model.Changed -= OnModelChanged;
        }

        BatteryReplacementRequested = null;
        _model = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_requiresInputRelease &&
            !Input.IsActionPressed("toggle_flashlight") &&
            !Input.IsActionPressed("replace_battery"))
        {
            _requiresInputRelease = false;
        }

        if (!_isActorAlive || _model is null || !_model.IsOn ||
            !double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        double drainAmount = _model.DrainPerSecond * delta;
        if (!double.IsFinite(drainAmount) || drainAmount <= 0.0)
        {
            throw new InvalidOperationException(
                "Validated 3D flashlight drain produced an invalid amount.");
        }

        _model.Drain(drainAmount);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Echo: true } ||
            !IsFlashlightInputEnabled ||
            _requiresInputRelease)
        {
            return;
        }

        if (@event.IsActionPressed("toggle_flashlight"))
        {
            Model.Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("replace_battery"))
        {
            SafeEventPublisher.Publish(
                BatteryReplacementRequested,
                $"{nameof(PlayerFlashlightController3D)}.{nameof(BatteryReplacementRequested)}");
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetFlashlightInputEnabled(bool enabled)
    {
        bool canEnable = enabled && _isActorAlive;
        if (canEnable && !_isFlashlightInputEnabled)
        {
            _requiresInputRelease = true;
        }

        _isFlashlightInputEnabled = canEnable;
    }

    public void SetActorAlive(bool isAlive)
    {
        _isActorAlive = isAlive;
        if (!isAlive)
        {
            _isFlashlightInputEnabled = false;
            _requiresInputRelease = true;
            _model?.TurnOff();
        }

        ApplyPresentation();
    }

    public FlashlightStateChangeResult TurnOff()
    {
        return Model.TurnOff();
    }

    private void OnModelChanged()
    {
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        bool enabled = _model is not null && _model.IsOn && _isActorAlive;
        if (GodotObject.IsInstanceValid(_spotLight))
        {
            _spotLight.Visible = enabled;
        }
    }
}
