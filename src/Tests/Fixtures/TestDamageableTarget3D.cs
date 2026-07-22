using Godot;
using LineZero.Gameplay.Health;

namespace LineZero.Tests.Fixtures;

public sealed partial class TestDamageableTarget3D : StaticBody3D, IHealthOwner
{
    public TestDamageableTarget3D()
    {
        Health = new HealthModel(100);
    }

    public HealthModel Health { get; }
}
