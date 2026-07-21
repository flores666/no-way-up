using System;
using Godot;

namespace LineZero.Gameplay.Inventory;

public sealed partial class InventoryComponent : Node
{
    private InventoryModel? _inventory;

    [Export(PropertyHint.Range, "1,128,1,or_greater")]
    public int SlotCapacity { get; set; } = 12;

    [Export]
    public Godot.Collections.Array<InventorySeedEntry> InitialContents { get; set; } = new();

    public InventoryModel Inventory => _inventory
        ?? throw new InvalidOperationException(
            $"{nameof(InventoryComponent)} on '{Name}' has not been initialized.");

    public override void _Ready()
    {
        if (SlotCapacity < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(InventoryComponent)} on '{Name}' requires a positive slot capacity.");
        }

        InventoryModel inventory = new(SlotCapacity);
        for (int index = 0; index < InitialContents.Count; index++)
        {
            InventorySeedEntry? entry = InitialContents[index];
            if (entry is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(InventoryComponent)} on '{Name}' has a null initial-content entry at index {index}.");
            }

            entry.Validate();
            InventoryAddResult result = inventory.TryAdd(entry.Item!, entry.Quantity);
            if (!result.WasFullyAdded)
            {
                throw new InvalidOperationException(
                    $"{nameof(InventoryComponent)} on '{Name}' could not seed " +
                    $"{entry.Item!.DisplayName} x{entry.Quantity}; " +
                    $"{result.RemainingQuantity} units exceed its slot capacity.");
            }
        }

        _inventory = inventory;
    }
}
