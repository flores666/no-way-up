namespace LineZero.World3D;

public static class CollisionLayers3D
{
    public const uint World = 1u << 0;
    public const uint PlayerMovementBody = 1u << 1;
    public const uint MutantMovementBody = 1u << 2;
    public const uint FirearmTarget = 1u << 3;
    public const uint InteractionArea = 1u << 4;
    public const uint AimSurface = 1u << 5;
    public const uint VisibilityZone = 1u << 6;
    public const uint HazardZone = 1u << 7;
    public const uint ObjectiveZone = 1u << 8;
    public const uint PlayerInteractionSensor = 1u << 9;
    public const uint PlayerVisibilitySensor = 1u << 10;
    public const uint PlayerHazardSensor = 1u << 11;
    public const uint PlayerObjectiveSensor = 1u << 12;
    public const uint CameraOccluder = 1u << 13;

    public const uint MovementObstacles = World | MutantMovementBody;
    public const uint ShotObstaclesAndTargets =
        World | MutantMovementBody | FirearmTarget;
}
