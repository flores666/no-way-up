using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Inventory;

namespace LineZero.Tests.Fixtures;

public sealed partial class TestHealthInventoryActorNode : Node, IHealthOwner, IInventoryOwner
{
    public TestHealthInventoryActorNode()
    {
        Health = new HealthModel(100);
        Inventory = new InventoryModel(8);
    }

    public HealthModel Health { get; }

    public InventoryModel Inventory { get; }
}
