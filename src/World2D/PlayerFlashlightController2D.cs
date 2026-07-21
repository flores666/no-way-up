using System;
using Godot;
using LineZero.Gameplay.Flashlight;

namespace LineZero.World2D;

public sealed partial class PlayerFlashlightController2D : Node2D
{
    private FlashlightModel? _model;
    private bool _isFlashlightInputEnabled = true;
    private bool _isActorAlive = true;
    private bool _requiresInputRelease;

    [Export]
    public FlashlightDefinition? FlashlightDefinition { get; set; }

    [Export]
    public bool StartOn { get; set; } = true;

    public FlashlightModel Model => _model
        ?? throw new InvalidOperationException(
            $"{nameof(PlayerFlashlightController2D)} on '{Name}' has no initialized model.");

    public bool IsFlashlightInputEnabled =>
        _isFlashlightInputEnabled && _isActorAlive;

    public event Action? BatteryReplacementRequested;

    public override void _Ready()
    {
        FlashlightDefinition definition = FlashlightDefinition
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerFlashlightController2D)} on '{Name}' requires a definition.");
        definition.Validate();

        PointLight2D pointLight = GetNodeOrNull<PointLight2D>("%FlashlightPointLight")
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerFlashlightController2D)} on '{Name}' requires " +
                "a unique FlashlightPointLight node.");
        if (!GodotObject.IsInstanceValid(pointLight))
        {
            throw new InvalidOperationException("The flashlight light node is invalid.");
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

        if (!_isActorAlive || _model is null || !_model.IsOn)
        {
            return;
        }

        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        double drainAmount = _model.DrainPerSecond * delta;
        if (!double.IsFinite(drainAmount) || drainAmount <= 0.0)
        {
            throw new InvalidOperationException(
                "Validated flashlight drain produced an invalid charge amount.");
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
            BatteryReplacementRequested?.Invoke();
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
        if (_isActorAlive == isAlive)
        {
            if (!isAlive && _model is not null)
            {
                _model.TurnOff();
            }

            return;
        }

        _isActorAlive = isAlive;
        if (!isAlive)
        {
            _isFlashlightInputEnabled = false;
            _requiresInputRelease = true;
            if (_model is not null)
            {
                _model.TurnOff();
            }
        }
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
        Visible = Model.IsOn && _isActorAlive;
    }
}
