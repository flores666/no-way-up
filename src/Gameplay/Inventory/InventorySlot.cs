using System;
using LineZero.Gameplay.Items;

namespace LineZero.Gameplay.Inventory;

public sealed class InventorySlot
{
    internal InventorySlot()
    {
    }

    public ItemDefinition? Item { get; private set; }

    public int Quantity { get; private set; }

    public bool IsEmpty => Item is null;

    internal bool ContainsItem(string itemId)
    {
        return Item is not null &&
               string.Equals(Item.Id, itemId, StringComparison.Ordinal);
    }

    internal int AddQuantity(int requestedQuantity)
    {
        Validate();

        ItemDefinition item = Item
            ?? throw new InvalidOperationException("Cannot add quantity to an empty slot.");

        if (requestedQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "Requested quantity must be at least one.");
        }

        int addedQuantity = Math.Min(requestedQuantity, item.MaxStackSize - Quantity);
        Quantity += addedQuantity;
        Validate();
        return addedQuantity;
    }

    internal void RemoveQuantity(int quantity)
    {
        Validate();

        if (Item is null)
        {
            throw new InvalidOperationException("Cannot remove quantity from an empty slot.");
        }

        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity to remove must be at least one.");
        }

        if (quantity > Quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                $"Cannot remove {quantity} units from a stack containing {Quantity}.");
        }

        Quantity -= quantity;
        if (Quantity == 0)
        {
            Item = null;
        }

        Validate();
    }

    internal void Assign(ItemDefinition item, int quantity)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Validate();

        if (!IsEmpty || Quantity != 0)
        {
            throw new InvalidOperationException("Only an empty slot can receive a new stack.");
        }

        Item = item;
        Quantity = quantity;
        Validate();
    }

    internal void Validate()
    {
        if (Item is null)
        {
            if (Quantity != 0)
            {
                throw new InvalidOperationException(
                    "An empty inventory slot must have a quantity of zero.");
            }

            return;
        }

        Item.Validate();
        if (Quantity < 1)
        {
            throw new InvalidOperationException(
                $"Inventory slot '{Item.Id}' must have a positive quantity.");
        }

        if (Quantity > Item.MaxStackSize)
        {
            throw new InvalidOperationException(
                $"Inventory slot '{Item.Id}' exceeds its maximum stack size.");
        }
    }
}
