using System;

namespace LineZero.Gameplay.Inventory;

public sealed class InventoryRemoveResult
{
    public InventoryRemoveResult(
        int requestedQuantity,
        int removedQuantity,
        int sourceQuantityAfterRemoval)
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

        if (sourceQuantityAfterRemoval < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceQuantityAfterRemoval),
                "Source quantity after removal cannot be negative.");
        }

        RequestedQuantity = requestedQuantity;
        RemovedQuantity = removedQuantity;
        SourceQuantityAfterRemoval = sourceQuantityAfterRemoval;
    }

    public int RequestedQuantity { get; }

    public int RemovedQuantity { get; }

    public int RemainingRequestedQuantity => RequestedQuantity - RemovedQuantity;

    public int SourceQuantityAfterRemoval { get; }

    public bool WasFullyRemoved => RemainingRequestedQuantity == 0;

    public bool RemovedNothing => RemovedQuantity == 0;
}
