using System;
using Godot;
using LineZero.Gameplay.Items;

namespace LineZero.Gameplay.Inventory;

[GlobalClass]
public sealed partial class InventorySeedEntry : Resource
{
    [Export]
    public ItemDefinition? Item { get; set; }

    [Export(PropertyHint.Range, "1,9999,1,or_greater")]
    public int Quantity { get; set; } = 1;

    public void Validate()
    {
        if (Item is null)
        {
            throw new InvalidOperationException(
                $"Inventory seed entry '{ResourcePath}' requires an item definition.");
        }

        Item.Validate();
        if (Quantity < 1)
        {
            throw new InvalidOperationException(
                $"Inventory seed entry for '{Item.Id}' requires a positive quantity.");
        }
    }
}
