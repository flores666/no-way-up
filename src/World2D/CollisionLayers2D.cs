namespace LineZero.World2D;

public static class CollisionLayers2D
{
    public const uint World = 1u << 0;
    public const uint Interaction = 1u << 1;
    public const uint DamageableTarget = 1u << 2;
    public const uint LightExposureSensor = 1u << 3;
    public const uint PlayerHazardSensor = 1u << 4;
    public const uint PlayerObjectiveSensor = 1u << 5;
}
