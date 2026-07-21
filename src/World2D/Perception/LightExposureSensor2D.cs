using System;
using Godot;
using LineZero.Core.Scene;
using LineZero.World2D;

namespace LineZero.World2D.Perception;

[GlobalClass]
public sealed partial class LightExposureSensor2D : Area2D
{
    private PlayerVisibilityController2D? _visibilityController;
    private CollisionShape2D? _sensorShape;

    [Export]
    public NodePath VisibilityTargetPath { get; set; } = new();

    [Export]
    public NodePath SensorShapePath { get; set; } = new();

    public override void _Ready()
    {
        PlayerVisibilityController2D visibilityController =
            RequiredNodePathResolver.Resolve<PlayerVisibilityController2D>(
                this,
                VisibilityTargetPath,
                nameof(VisibilityTargetPath));
        CollisionShape2D sensorShape =
            RequiredNodePathResolver.Resolve<CollisionShape2D>(
                this,
                SensorShapePath,
                nameof(SensorShapePath));

        ValidateShape(sensorShape);
        _visibilityController = visibilityController;
        _sensorShape = sensorShape;

        if (CollisionLayer != CollisionLayers2D.LightExposureSensor)
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureSensor2D)} on '{Name}' must use only the " +
                "dedicated light-exposure sensor layer.");
        }

        if (CollisionMask != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureSensor2D)} on '{Name}' must not detect " +
                "physics objects itself.");
        }

        if (Monitoring || !Monitorable)
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureSensor2D)} on '{Name}' must be non-monitoring " +
                "and monitorable so authored exposure zones can detect it.");
        }
    }

    public override void _ExitTree()
    {
        _visibilityController = null;
        _sensorShape = null;
    }

    public bool TryGetVisibilityController(
        out PlayerVisibilityController2D? visibilityController)
    {
        visibilityController = _visibilityController;
        return visibilityController is not null &&
               GodotObject.IsInstanceValid(visibilityController) &&
               visibilityController.IsInsideTree();
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
                $"{nameof(LightExposureSensor2D)} on '{Name}' requires one enabled " +
                "direct-child CollisionShape2D with a configured shape.");
        }
    }
}
