namespace LineZero.Gameplay.Inventory;

public interface IInventoryContainer : IInventoryOwner
{
    string ContainerDisplayName { get; }
}
