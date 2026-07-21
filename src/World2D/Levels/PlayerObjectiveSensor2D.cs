using System;
using Godot;
using LineZero.Core.Scene;
using LineZero.World2D;

namespace LineZero.World2D.Levels;

[GlobalClass]
public sealed partial class PlayerObjectiveSensor2D : Area2D
{
    private PlayerController2D? _player;
    private CollisionShape2D? _sensorShape;

    [Export]
    public NodePath PlayerTargetPath { get; set; } = new();

    [Export]
    public NodePath SensorShapePath { get; set; } = new();

    public bool IsLivingEligiblePlayer =>
        TryGetPlayer(out PlayerController2D? player) &&
        player is not null &&
        player.Health.IsAlive;

    public override void _Ready()
    {
        PlayerController2D player =
            RequiredNodePathResolver.Resolve<PlayerController2D>(
                this,
                PlayerTargetPath,
                nameof(PlayerTargetPath));
        CollisionShape2D sensorShape =
            RequiredNodePathResolver.Resolve<CollisionShape2D>(
                this,
                SensorShapePath,
                nameof(SensorShapePath));

        if (GetParent()?.GetInstanceId() != player.GetInstanceId())
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerObjectiveSensor2D)} on '{Name}' must be a direct child " +
                "of its explicitly assigned player.");
        }

        ValidateShape(sensorShape);
        _player = player;
        _sensorShape = sensorShape;

        if (CollisionLayer != CollisionLayers2D.PlayerObjectiveSensor)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerObjectiveSensor2D)} on '{Name}' must use only the " +
                "dedicated player-objective sensor layer.");
        }

        if (CollisionMask != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerObjectiveSensor2D)} on '{Name}' must not detect " +
                "physics objects itself.");
        }

        if (Monitoring || !Monitorable)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerObjectiveSensor2D)} on '{Name}' must be non-monitoring " +
                "and monitorable so objective zones can detect it.");
        }
    }

    public override void _ExitTree()
    {
        _player = null;
        _sensorShape = null;
    }

    public bool TryGetPlayer(out PlayerController2D? player)
    {
        player = _player;
        return player is not null &&
               GodotObject.IsInstanceValid(player) &&
               player.IsInsideTree();
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
                $"{nameof(PlayerObjectiveSensor2D)} on '{Name}' requires one enabled " +
                "direct-child CollisionShape2D with a configured shape.");
        }
    }
}
