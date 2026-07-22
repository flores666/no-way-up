using System;
using System.Collections.Generic;
using Godot;

namespace LineZero.World3D.Perception;

public sealed partial class LightExposureZone3D : Area3D
{
    private readonly HashSet<PlayerVisibilitySensor3D> _sensors = new();
    private bool _exposureEnabled = true;
    private bool _isReady;

    [Export]
    public string DisplayName { get; set; } = "Light zone";

    [Export(PropertyHint.Range, "0.01,5.0,0.01")]
    public float VisibilityMultiplier { get; set; } = 1.0f;

    [Export]
    public int ZonePriority { get; set; }

    [Export]
    public bool ExposureEnabled
    {
        get => _exposureEnabled;
        set
        {
            if (_exposureEnabled == value)
            {
                return;
            }

            _exposureEnabled = value;
            if (_isReady)
            {
                ApplyExposureStateToSensors();
            }
        }
    }

    public override void _Ready()
    {
        ValidateConfiguration();
        CollisionShape3D shape =
            GetNodeOrNull<CollisionShape3D>("%LightExposureShape3D")
            ?? throw new InvalidOperationException(
                $"{nameof(LightExposureZone3D)} on '{Name}' requires a shape.");
        if (shape.Shape is null || shape.Disabled ||
            CollisionLayer != CollisionLayers3D.VisibilityZone ||
            CollisionMask != CollisionLayers3D.PlayerVisibilitySensor ||
            !Monitoring ||
            Monitorable)
        {
            throw new InvalidOperationException(
                "LightExposureZone3D has invalid dedicated collision settings.");
        }

        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
        _isReady = true;
    }

    public override void _ExitTree()
    {
        AreaEntered -= OnAreaEntered;
        AreaExited -= OnAreaExited;
        foreach (PlayerVisibilitySensor3D sensor in _sensors)
        {
            ExitSensorZoneIfValid(sensor);
        }

        _sensors.Clear();
        _isReady = false;
    }

    public void SetExposureEnabled(bool enabled)
    {
        ExposureEnabled = enabled;
    }

    public void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(DisplayName) ||
            !float.IsFinite(VisibilityMultiplier) ||
            VisibilityMultiplier <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureZone3D)} on '{Name}' has invalid configuration.");
        }
    }

    private void OnAreaEntered(Area3D area)
    {
        if (area is not PlayerVisibilitySensor3D sensor || !_sensors.Add(sensor))
        {
            return;
        }

        if (ExposureEnabled &&
            sensor.TryGetVisibilityController(out PlayerVisibilityController3D? target) &&
            target is not null)
        {
            target.EnterZone(this);
        }
    }

    private void OnAreaExited(Area3D area)
    {
        if (area is not PlayerVisibilitySensor3D sensor || !_sensors.Remove(sensor))
        {
            return;
        }

        if (ExposureEnabled)
        {
            ExitSensorZoneIfValid(sensor);
        }
    }

    private void ApplyExposureStateToSensors()
    {
        foreach (PlayerVisibilitySensor3D sensor in _sensors)
        {
            if (!GodotObject.IsInstanceValid(sensor) ||
                !sensor.IsInsideTree() ||
                !sensor.TryGetVisibilityController(
                    out PlayerVisibilityController3D? target) ||
                target is null)
            {
                continue;
            }

            if (ExposureEnabled)
            {
                target.EnterZone(this);
            }
            else
            {
                target.ExitZone(this);
            }
        }
    }

    private void ExitSensorZoneIfValid(PlayerVisibilitySensor3D sensor)
    {
        if (GodotObject.IsInstanceValid(sensor) &&
            sensor.IsInsideTree() &&
            sensor.TryGetVisibilityController(
                out PlayerVisibilityController3D? target) &&
            target is not null)
        {
            target.ExitZone(this);
        }
    }
}
