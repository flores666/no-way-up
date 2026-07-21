using System;

namespace LineZero.Gameplay.Inventory;

public sealed class InventoryAddResult
{
    public InventoryAddResult(int requestedQuantity, int addedQuantity)
    {
        if (requestedQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "Requested quantity must be at least one.");
        }

        if (addedQuantity < 0 || addedQuantity > requestedQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(addedQuantity),
                "Added quantity must be between zero and the requested quantity.");
        }

        RequestedQuantity = requestedQuantity;
        AddedQuantity = addedQuantity;
        RemainingQuantity = requestedQuantity - addedQuantity;
    }

    public int RequestedQuantity { get; }

    public int AddedQuantity { get; }

    public int RemainingQuantity { get; }

    public bool WasFullyAdded => RemainingQuantity == 0;

    public bool AddedNothing => AddedQuantity == 0;
}
