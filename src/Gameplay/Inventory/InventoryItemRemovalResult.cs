using System;

namespace LineZero.Gameplay.Inventory;

public sealed class InventoryItemRemovalResult
{
    public InventoryItemRemovalResult(
        int requestedQuantity,
        int removedQuantity,
        int remainingItemQuantity)
    {
        if (requestedQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "Requested quantity must be at least one.");
        }

        if (removedQuantity < 0 || removedQuantity > requestedQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(removedQuantity),
                "Removed quantity must be within the requested quantity.");
        }

        if (remainingItemQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingItemQuantity),
                "Remaining item quantity cannot be negative.");
        }

        RequestedQuantity = requestedQuantity;
        RemovedQuantity = removedQuantity;
        RemainingItemQuantity = remainingItemQuantity;
    }

    public int RequestedQuantity { get; }

    public int RemovedQuantity { get; }

    public int RemainingRequestedQuantity => RequestedQuantity - RemovedQuantity;

    public int RemainingItemQuantity { get; }

    public bool WasFullyRemoved => RemainingRequestedQuantity == 0;

    public bool RemovedNothing => RemovedQuantity == 0;
}
