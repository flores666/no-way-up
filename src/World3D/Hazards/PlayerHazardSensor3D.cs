using System;
using Godot;
using LineZero.Gameplay.Health;

namespace LineZero.World3D.Hazards;

public sealed partial class PlayerHazardSensor3D : Area3D
{
    private HealthModel? _health;

    public override void _Ready()
    {
        CollisionShape3D sensorShape =
            GetNodeOrNull<CollisionShape3D>("%PlayerHazardSensorShape3D")
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerHazardSensor3D)} on '{Name}' requires a sensor shape.");
        if (sensorShape.Shape is null || sensorShape.Disabled)
        {
            throw new InvalidOperationException(
                "PlayerHazardSensor3D requires one enabled constant shape.");
        }

        if (CollisionLayer != CollisionLayers3D.PlayerHazardSensor ||
            CollisionMask != 0 ||
            Monitoring ||
            !Monitorable)
        {
            throw new InvalidOperationException(
                "PlayerHazardSensor3D has invalid dedicated collision settings.");
        }
    }

    public override void _ExitTree()
    {
        _health = null;
    }

    public void Bind(HealthModel health)
    {
        ArgumentNullException.ThrowIfNull(health);
        if (_health is not null && !ReferenceEquals(_health, health))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerHazardSensor3D)} on '{Name}' is already bound.");
        }

        _health = health;
    }

    public bool TryGetHealth(out HealthModel? health)
    {
        health = _health;
        return health is not null;
    }
}
