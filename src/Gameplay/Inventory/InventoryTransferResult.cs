using System;

namespace LineZero.Gameplay.Inventory;

public sealed class InventoryTransferResult
{
    public InventoryTransferResult(
        int requestedQuantity,
        int transferredQuantity,
        int sourceQuantityAfterTransfer)
    {
        if (requestedQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "Requested quantity must be at least one.");
        }

        if (transferredQuantity < 0 || transferredQuantity > requestedQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transferredQuantity),
                "Transferred quantity must be within the requested quantity.");
        }

        if (sourceQuantityAfterTransfer < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceQuantityAfterTransfer),
                "Source quantity after transfer cannot be negative.");
        }

        RequestedQuantity = requestedQuantity;
        TransferredQuantity = transferredQuantity;
        SourceQuantityAfterTransfer = sourceQuantityAfterTransfer;
    }

    public int RequestedQuantity { get; }

    public int TransferredQuantity { get; }

    public int RemainingRequestedQuantity => RequestedQuantity - TransferredQuantity;

    public int SourceQuantityAfterTransfer { get; }

    public bool WasFullyTransferred => RemainingRequestedQuantity == 0;

    public bool TransferredNothing => TransferredQuantity == 0;
}
