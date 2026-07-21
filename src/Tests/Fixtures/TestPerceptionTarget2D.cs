using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Perception;

namespace LineZero.Tests.Fixtures;

public sealed partial class TestPerceptionTarget2D : Node2D, IHealthOwner, IVisibilityTarget
{
    public HealthModel Health { get; } = new(100);

    public float VisibilityMultiplier { get; set; } = 1.0f;
}
