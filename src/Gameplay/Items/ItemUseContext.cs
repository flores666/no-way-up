using System;
using Godot;
using LineZero.Gameplay.Inventory;

namespace LineZero.Gameplay.Items;

public sealed class ItemUseContext
{
    public ItemUseContext(Node actor, InventoryModel inventory, int sourceSlotIndex)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(inventory);

        if (!GodotObject.IsInstanceValid(actor))
        {
            throw new ArgumentException(
                "Item-use actor must be a valid Godot node.",
                nameof(actor));
        }

        if (sourceSlotIndex < 0 || sourceSlotIndex >= inventory.Capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceSlotIndex),
                $"Source slot index must be between 0 and {inventory.Capacity - 1}.");
        }

        Actor = actor;
        Inventory = inventory;
        SourceSlotIndex = sourceSlotIndex;
    }

    public Node Actor { get; }

    public InventoryModel Inventory { get; }

    public int SourceSlotIndex { get; }
}
