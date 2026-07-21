using System;
using Godot;
using LineZero.Core.Scene;
using LineZero.Gameplay.Health;
using LineZero.World2D;

namespace LineZero.World2D.Hazards;

[GlobalClass]
public sealed partial class PlayerHazardSensor2D : Area2D
{
    private HealthComponent? _healthComponent;
    private CollisionShape2D? _sensorShape;

    [Export]
    public NodePath HealthTargetPath { get; set; } = new();

    [Export]
    public NodePath SensorShapePath { get; set; } = new();

    public override void _Ready()
    {
        HealthComponent healthComponent =
            RequiredNodePathResolver.Resolve<HealthComponent>(
                this,
                HealthTargetPath,
                nameof(HealthTargetPath));
        CollisionShape2D sensorShape =
            RequiredNodePathResolver.Resolve<CollisionShape2D>(
                this,
                SensorShapePath,
                nameof(SensorShapePath));

        ValidateShape(sensorShape);
        _healthComponent = healthComponent;
        _sensorShape = sensorShape;

        if (CollisionLayer != CollisionLayers2D.PlayerHazardSensor)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerHazardSensor2D)} on '{Name}' must use only the " +
                "dedicated player-hazard sensor layer.");
        }

        if (CollisionMask != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerHazardSensor2D)} on '{Name}' must not detect " +
                "physics objects itself.");
        }

        if (Monitoring || !Monitorable)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerHazardSensor2D)} on '{Name}' must be non-monitoring " +
                "and monitorable so authored damage zones can detect it.");
        }
    }

    public override void _ExitTree()
    {
        _healthComponent = null;
        _sensorShape = null;
    }

    public bool TryGetHealth(out HealthModel? health)
    {
        HealthComponent? component = _healthComponent;
        if (component is null ||
            !GodotObject.IsInstanceValid(component) ||
            !component.IsInsideTree())
        {
            health = null;
            return false;
        }

        health = component.Health;
        return true;
    }

    private void ValidateShape(CollisionShape2D sensorShape)
    {
        Node? shapeParent = sensorShape.GetParent();
        if (shapeParent is null ||
            shapeParent.GetInstanceId() != GetInstanceId() ||
            sensorShape.Shape is null ||
            sensorShape.Disabled)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerHazardSensor2D)} on '{Name}' requires one enabled " +
                "direct-child CollisionShape2D with a configured shape.");
        }
    }
}
