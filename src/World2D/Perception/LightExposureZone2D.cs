using System;
using System.Collections.Generic;
using Godot;
using LineZero.World2D;

namespace LineZero.World2D.Perception;

[GlobalClass]
public sealed partial class LightExposureZone2D : Area2D
{
    private readonly HashSet<LightExposureSensor2D> _sensors = new();
    private string _displayName = "Light zone";
    private bool _exposureEnabled = true;
    private bool _isReady;

    [Export]
    public string DisplayName
    {
        get => _displayName;
        set => _displayName = value ?? string.Empty;
    }

    [Export(PropertyHint.Range, "0.01,5.0,0.01,or_greater")]
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
        if (CollisionLayer != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureZone2D)} on '{Name}' must not occupy a collision layer.");
        }

        if (CollisionMask != CollisionLayers2D.LightExposureSensor)
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureZone2D)} on '{Name}' must detect only the " +
                "dedicated player light-exposure sensor layer.");
        }

        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
        _isReady = true;
    }

    public override void _ExitTree()
    {
        AreaEntered -= OnAreaEntered;
        AreaExited -= OnAreaExited;
        foreach (LightExposureSensor2D sensor in _sensors)
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
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureZone2D)} on '{Name}' requires a display name.");
        }

        if (!float.IsFinite(VisibilityMultiplier) || VisibilityMultiplier <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureZone2D)} on '{Name}' requires a positive finite " +
                "visibility multiplier.");
        }
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area is not LightExposureSensor2D sensor || !_sensors.Add(sensor))
        {
            return;
        }

        if (ExposureEnabled &&
            sensor.TryGetVisibilityController(
                out PlayerVisibilityController2D? visibilityController) &&
            visibilityController is not null)
        {
            visibilityController.EnterZone(this);
        }
    }

    private void OnAreaExited(Area2D area)
    {
        if (area is not LightExposureSensor2D sensor || !_sensors.Remove(sensor))
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
        List<LightExposureSensor2D>? staleSensors = null;
        foreach (LightExposureSensor2D sensor in _sensors)
        {
            if (!GodotObject.IsInstanceValid(sensor) || !sensor.IsInsideTree())
            {
                staleSensors ??= new List<LightExposureSensor2D>();
                staleSensors.Add(sensor);
                continue;
            }

            if (!sensor.TryGetVisibilityController(
                    out PlayerVisibilityController2D? target) ||
                target is null)
            {
                staleSensors ??= new List<LightExposureSensor2D>();
                staleSensors.Add(sensor);
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

        if (staleSensors is null)
        {
            return;
        }

        for (int index = 0; index < staleSensors.Count; index++)
        {
            _sensors.Remove(staleSensors[index]);
        }
    }

    private void ExitSensorZoneIfValid(LightExposureSensor2D sensor)
    {
        if (!GodotObject.IsInstanceValid(sensor) || !sensor.IsInsideTree())
        {
            return;
        }

        if (sensor.TryGetVisibilityController(
                out PlayerVisibilityController2D? target) &&
            target is not null)
        {
            target.ExitZone(this);
        }
    }
}
