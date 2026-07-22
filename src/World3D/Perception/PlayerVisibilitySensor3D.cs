using System;
using Godot;

namespace LineZero.World3D.Perception;

public sealed partial class PlayerVisibilitySensor3D : Area3D
{
    private PlayerVisibilityController3D? _visibilityController;

    public override void _Ready()
    {
        CollisionShape3D sensorShape =
            GetNodeOrNull<CollisionShape3D>("%PlayerVisibilitySensorShape3D")
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerVisibilitySensor3D)} on '{Name}' requires a sensor shape.");
        if (sensorShape.Shape is null || sensorShape.Disabled)
        {
            throw new InvalidOperationException(
                "PlayerVisibilitySensor3D requires one enabled constant shape.");
        }

        if (CollisionLayer != CollisionLayers3D.PlayerVisibilitySensor ||
            CollisionMask != 0 ||
            Monitoring ||
            !Monitorable)
        {
            throw new InvalidOperationException(
                "PlayerVisibilitySensor3D has invalid dedicated collision settings.");
        }
    }

    public override void _ExitTree()
    {
        _visibilityController = null;
    }

    public void Bind(PlayerVisibilityController3D visibilityController)
    {
        ArgumentNullException.ThrowIfNull(visibilityController);
        if (_visibilityController is not null &&
            !ReferenceEquals(_visibilityController, visibilityController))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerVisibilitySensor3D)} on '{Name}' is already bound.");
        }

        _visibilityController = visibilityController;
    }

    public bool TryGetVisibilityController(
        out PlayerVisibilityController3D? visibilityController)
    {
        visibilityController = _visibilityController;
        return visibilityController is not null;
    }
}
