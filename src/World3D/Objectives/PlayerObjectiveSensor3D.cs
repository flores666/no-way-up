using System;
using Godot;

namespace LineZero.World3D.Objectives;

public sealed partial class PlayerObjectiveSensor3D : Area3D
{
    private PlayerController3D? _player;

    public bool IsLivingEligiblePlayer =>
        TryGetPlayer(out PlayerController3D? player) &&
        player is not null &&
        player.Health.IsAlive &&
        !player.IsTerminalState;

    public override void _Ready()
    {
        CollisionShape3D sensorShape =
            GetNodeOrNull<CollisionShape3D>("%PlayerObjectiveSensorShape3D")
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerObjectiveSensor3D)} on '{Name}' requires a sensor shape.");
        if (sensorShape.Shape is null || sensorShape.Disabled ||
            sensorShape.GetParent() != this)
        {
            throw new InvalidOperationException(
                "PlayerObjectiveSensor3D requires one enabled constant direct-child shape.");
        }

        if (CollisionLayer != CollisionLayers3D.PlayerObjectiveSensor ||
            CollisionMask != 0 ||
            Monitoring ||
            !Monitorable)
        {
            throw new InvalidOperationException(
                "PlayerObjectiveSensor3D has invalid dedicated collision settings.");
        }
    }

    public override void _ExitTree()
    {
        _player = null;
    }

    public void Bind(PlayerController3D player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.GetParent() is null || GetParent() != player)
        {
            throw new ArgumentException(
                "The objective sensor must be a direct child of its player.",
                nameof(player));
        }

        if (_player is not null && !ReferenceEquals(_player, player))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerObjectiveSensor3D)} on '{Name}' is already bound.");
        }

        _player = player;
    }

    public bool TryGetPlayer(out PlayerController3D? player)
    {
        player = _player;
        return player is not null &&
               GodotObject.IsInstanceValid(player) &&
               player.IsInsideTree();
    }
}
