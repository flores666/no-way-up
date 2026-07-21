using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LineZero.Core.Events;
using LineZero.Gameplay.Items;

namespace LineZero.Gameplay.Inventory;

internal readonly record struct InventorySingleItemRemovalPlan(
    InventoryModel Inventory,
    int SlotIndex,
    ItemDefinition? Item,
    int QuantityBefore);

internal readonly record struct InventoryRemovalSlice(
    int SlotIndex,
    ItemDefinition Item,
    int QuantityBefore,
    int QuantityToRemove);

internal sealed class InventoryItemQuantityRemovalPlan
{
    public InventoryItemQuantityRemovalPlan(
        InventoryModel inventory,
        string itemId,
        int requestedQuantity,
        int availableQuantityBefore,
        InventoryRemovalSlice[] slices)
    {
        Inventory = inventory;
        ItemId = itemId;
        RequestedQuantity = requestedQuantity;
        AvailableQuantityBefore = availableQuantityBefore;
        Slices = slices;
    }

    public InventoryModel Inventory { get; }

    public string ItemId { get; }

    public int RequestedQuantity { get; }

    public int AvailableQuantityBefore { get; }

    public InventoryRemovalSlice[] Slices { get; }
}

public sealed class InventoryModel
{
    private readonly InventorySlot[] _slots;
    private readonly ReadOnlyCollection<InventorySlot> _readOnlySlots;

    public InventoryModel(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Inventory capacity must be at least one slot.");
        }

        _slots = new InventorySlot[capacity];
        for (int index = 0; index < _slots.Length; index++)
        {
            _slots[index] = new InventorySlot();
        }

        _readOnlySlots = Array.AsReadOnly(_slots);
    }

    public int Capacity => _slots.Length;

    public IReadOnlyList<InventorySlot> Slots => _readOnlySlots;

    public event Action? Changed;

    public int CountByItemId(string itemId)
    {
        ValidateItemId(itemId);
        ValidateSlots();

        int totalQuantity = 0;
        for (int index = 0; index < _slots.Length; index++)
        {
            InventorySlot slot = _slots[index];
            if (!slot.ContainsItem(itemId))
            {
                continue;
            }

            totalQuantity = checked(totalQuantity + slot.Quantity);
        }

        return totalQuantity;
    }

    public InventoryAddResult TryAdd(ItemDefinition item, int quantity)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Validate();

        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity to add must be at least one.");
        }

        ValidateSlots();

        int addedQuantity = AddToSlots(item, quantity);
        InventoryAddResult result = new(quantity, addedQuantity);
        if (!result.AddedNothing)
        {
            PublishChanged();
        }

        return result;
    }

    public InventoryRemoveResult TryRemoveFromSlot(
        int slotIndex,
        int requestedQuantity)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex),
                $"Slot index must be between 0 and {_slots.Length - 1}.");
        }

        if (requestedQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "Requested removal quantity must be at least one.");
        }

        ValidateSlots();

        InventorySlot slot = _slots[slotIndex];
        if (slot.IsEmpty)
        {
            return new InventoryRemoveResult(requestedQuantity, 0, 0);
        }

        int removedQuantity = Math.Min(requestedQuantity, slot.Quantity);
        slot.RemoveQuantity(removedQuantity);

        InventoryRemoveResult result = new(
            requestedQuantity,
            removedQuantity,
            slot.Quantity);
        PublishChanged();
        return result;
    }

    public InventoryItemRemovalResult TryRemoveByItemId(
        string itemId,
        int requestedQuantity)
    {
        InventoryItemRemovalResult result = TryRemoveByItemIdWithoutNotification(
            itemId,
            requestedQuantity);
        if (result.RemovedQuantity > 0)
        {
            PublishChanged();
        }

        return result;
    }

    internal bool TryPrepareSingleItemRemoval(
        string itemId,
        out InventorySingleItemRemovalPlan plan)
    {
        ValidateItemId(itemId);
        ValidateSlots();

        for (int index = 0; index < _slots.Length; index++)
        {
            InventorySlot slot = _slots[index];
            if (!slot.ContainsItem(itemId))
            {
                continue;
            }

            ItemDefinition item = slot.Item
                ?? throw new InvalidOperationException(
                    "A populated inventory slot must reference an item definition.");
            plan = new InventorySingleItemRemovalPlan(
                this,
                index,
                item,
                slot.Quantity);
            return true;
        }

        plan = default;
        return false;
    }

    internal bool TryPrepareSingleItemRemovalFromSlot(
        int slotIndex,
        ItemDefinition expectedItem,
        out InventorySingleItemRemovalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(expectedItem);
        if (slotIndex < 0 || slotIndex >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex),
                $"Slot index must be between 0 and {_slots.Length - 1}.");
        }

        ValidateSlots();
        InventorySlot slot = _slots[slotIndex];
        if (slot.IsEmpty || !ReferenceEquals(slot.Item, expectedItem))
        {
            plan = default;
            return false;
        }

        plan = new InventorySingleItemRemovalPlan(
            this,
            slotIndex,
            expectedItem,
            slot.Quantity);
        return true;
    }

    internal bool TryPrepareItemRemoval(
        string itemId,
        int requestedQuantity,
        out InventoryItemQuantityRemovalPlan? plan)
    {
        ValidateItemId(itemId);
        if (requestedQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "Requested removal quantity must be at least one.");
        }

        ValidateSlots();
        int availableQuantity = CountByItemId(itemId);
        if (availableQuantity < requestedQuantity)
        {
            plan = null;
            return false;
        }

        List<InventoryRemovalSlice> slices = new();
        int remaining = requestedQuantity;
        for (int index = 0; index < _slots.Length && remaining > 0; index++)
        {
            InventorySlot slot = _slots[index];
            if (!slot.ContainsItem(itemId))
            {
                continue;
            }

            ItemDefinition item = slot.Item
                ?? throw new InvalidOperationException(
                    "A populated inventory slot must reference an item definition.");
            int quantityToRemove = Math.Min(remaining, slot.Quantity);
            slices.Add(new InventoryRemovalSlice(
                index,
                item,
                slot.Quantity,
                quantityToRemove));
            remaining -= quantityToRemove;
        }

        if (remaining != 0)
        {
            throw new InvalidOperationException(
                "Inventory removal planning did not cover the requested quantity.");
        }

        plan = new InventoryItemQuantityRemovalPlan(
            this,
            itemId,
            requestedQuantity,
            availableQuantity,
            slices.ToArray());
        return true;
    }

    internal bool CanApply(InventorySingleItemRemovalPlan plan)
    {
        if (!ReferenceEquals(plan.Inventory, this) ||
            plan.SlotIndex < 0 ||
            plan.SlotIndex >= _slots.Length ||
            plan.Item is null ||
            plan.QuantityBefore < 1)
        {
            return false;
        }

        InventorySlot slot = _slots[plan.SlotIndex];
        return ReferenceEquals(slot.Item, plan.Item) &&
               slot.Quantity == plan.QuantityBefore;
    }

    internal void ApplyWithoutNotification(InventorySingleItemRemovalPlan plan)
    {
        if (!CanApply(plan))
        {
            throw new InvalidOperationException(
                "The prepared inventory removal is no longer valid.");
        }

        _slots[plan.SlotIndex].RemoveQuantity(1);
    }

    internal bool CanApply(InventoryItemQuantityRemovalPlan plan)
    {
        if (!ReferenceEquals(plan.Inventory, this) ||
            plan.RequestedQuantity < 1 ||
            plan.AvailableQuantityBefore < plan.RequestedQuantity ||
            string.IsNullOrWhiteSpace(plan.ItemId) ||
            plan.Slices.Length == 0)
        {
            return false;
        }

        int plannedQuantity = 0;
        for (int index = 0; index < plan.Slices.Length; index++)
        {
            InventoryRemovalSlice slice = plan.Slices[index];
            if (slice.SlotIndex < 0 ||
                slice.SlotIndex >= _slots.Length ||
                slice.QuantityBefore < 1 ||
                slice.QuantityToRemove < 1 ||
                slice.QuantityToRemove > slice.QuantityBefore)
            {
                return false;
            }

            InventorySlot slot = _slots[slice.SlotIndex];
            if (!ReferenceEquals(slot.Item, slice.Item) ||
                !slot.ContainsItem(plan.ItemId) ||
                slot.Quantity != slice.QuantityBefore)
            {
                return false;
            }

            plannedQuantity = checked(plannedQuantity + slice.QuantityToRemove);
        }

        return plannedQuantity == plan.RequestedQuantity &&
               CountByItemId(plan.ItemId) == plan.AvailableQuantityBefore;
    }

    internal InventoryItemRemovalResult ApplyWithoutNotification(
        InventoryItemQuantityRemovalPlan plan)
    {
        if (!CanApply(plan))
        {
            throw new InvalidOperationException(
                "The prepared inventory quantity removal is no longer valid.");
        }

        for (int index = 0; index < plan.Slices.Length; index++)
        {
            InventoryRemovalSlice slice = plan.Slices[index];
            _slots[slice.SlotIndex].RemoveQuantity(slice.QuantityToRemove);
        }

        return new InventoryItemRemovalResult(
            plan.RequestedQuantity,
            plan.RequestedQuantity,
            plan.AvailableQuantityBefore - plan.RequestedQuantity);
    }

    internal InventoryItemRemovalResult TryRemoveByItemIdWithoutNotification(
        string itemId,
        int requestedQuantity)
    {
        ValidateItemId(itemId);

        if (requestedQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "Requested removal quantity must be at least one.");
        }

        ValidateSlots();

        int availableQuantity = CountByItemId(itemId);
        int quantityToRemove = Math.Min(requestedQuantity, availableQuantity);
        if (quantityToRemove == 0)
        {
            return new InventoryItemRemovalResult(
                requestedQuantity,
                removedQuantity: 0,
                remainingItemQuantity: 0);
        }

        int remainingToRemove = quantityToRemove;
        for (int index = 0; index < _slots.Length && remainingToRemove > 0; index++)
        {
            InventorySlot slot = _slots[index];
            if (!slot.ContainsItem(itemId))
            {
                continue;
            }

            int removedFromSlot = Math.Min(remainingToRemove, slot.Quantity);
            slot.RemoveQuantity(removedFromSlot);
            remainingToRemove -= removedFromSlot;
        }

        if (remainingToRemove != 0)
        {
            throw new InvalidOperationException(
                "Inventory contents changed during deterministic item removal.");
        }

        return new InventoryItemRemovalResult(
            requestedQuantity,
            quantityToRemove,
            availableQuantity - quantityToRemove);
    }

    internal void PublishChanged()
    {
        SafeEventPublisher.Publish(
            Changed,
            $"{nameof(InventoryModel)}.{nameof(Changed)}");
    }

    public InventoryTransferResult TryTransferTo(
        InventoryModel destination,
        int sourceSlotIndex,
        int requestedQuantity)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (ReferenceEquals(this, destination))
        {
            throw new InvalidOperationException(
                "An inventory cannot transfer items to itself.");
        }

        if (sourceSlotIndex < 0 || sourceSlotIndex >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceSlotIndex),
                $"Source slot index must be between 0 and {_slots.Length - 1}.");
        }

        if (requestedQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "Requested transfer quantity must be at least one.");
        }

        ValidateSlots();
        destination.ValidateSlots();

        InventorySlot sourceSlot = _slots[sourceSlotIndex];
        if (sourceSlot.IsEmpty)
        {
            return new InventoryTransferResult(requestedQuantity, 0, 0);
        }

        ItemDefinition item = sourceSlot.Item
            ?? throw new InvalidOperationException(
                "A populated inventory slot must reference an item definition.");
        int sourceQuantityBeforeTransfer = sourceSlot.Quantity;
        int quantityAvailableForRequest = Math.Min(
            requestedQuantity,
            sourceQuantityBeforeTransfer);
        int transferableQuantity = destination.CalculateAcceptableQuantity(
            item,
            quantityAvailableForRequest);

        if (transferableQuantity == 0)
        {
            return new InventoryTransferResult(
                requestedQuantity,
                0,
                sourceQuantityBeforeTransfer);
        }

        int addedQuantity = destination.AddToSlots(item, transferableQuantity);
        if (addedQuantity != transferableQuantity)
        {
            throw new InvalidOperationException(
                "Inventory capacity changed during a transfer transaction.");
        }

        sourceSlot.RemoveQuantity(transferableQuantity);

        InventoryTransferResult result = new(
            requestedQuantity,
            transferableQuantity,
            sourceSlot.Quantity);

        // Both inventories are already consistent before observers are notified.
        destination.PublishChanged();
        PublishChanged();
        return result;
    }

    private int CalculateAcceptableQuantity(ItemDefinition item, int upperLimit)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Validate();

        if (upperLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(upperLimit),
                "The capacity calculation limit must be at least one.");
        }

        int remainingLimit = upperLimit;
        for (int index = 0; index < _slots.Length && remainingLimit > 0; index++)
        {
            InventorySlot slot = _slots[index];
            if (!slot.ContainsItem(item.Id))
            {
                continue;
            }

            ItemDefinition existingItem = slot.Item
                ?? throw new InvalidOperationException(
                    "A populated inventory slot must reference an item definition.");
            int availableStackSpace = existingItem.MaxStackSize - slot.Quantity;
            remainingLimit -= Math.Min(remainingLimit, availableStackSpace);
        }

        for (int index = 0; index < _slots.Length && remainingLimit > 0; index++)
        {
            if (!_slots[index].IsEmpty)
            {
                continue;
            }

            remainingLimit -= Math.Min(remainingLimit, item.MaxStackSize);
        }

        return upperLimit - remainingLimit;
    }

    private int AddToSlots(ItemDefinition item, int quantity)
    {
        int remainingQuantity = quantity;
        for (int index = 0; index < _slots.Length && remainingQuantity > 0; index++)
        {
            InventorySlot slot = _slots[index];
            if (!slot.ContainsItem(item.Id))
            {
                continue;
            }

            remainingQuantity -= slot.AddQuantity(remainingQuantity);
        }

        for (int index = 0; index < _slots.Length && remainingQuantity > 0; index++)
        {
            InventorySlot slot = _slots[index];
            if (!slot.IsEmpty)
            {
                continue;
            }

            int stackQuantity = Math.Min(remainingQuantity, item.MaxStackSize);
            slot.Assign(item, stackQuantity);
            remainingQuantity -= stackQuantity;
        }

        return quantity - remainingQuantity;
    }

    private void ValidateSlots()
    {
        for (int index = 0; index < _slots.Length; index++)
        {
            _slots[index].Validate();
        }
    }

    private static void ValidateItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new ArgumentException(
                "Item ID must be non-empty.",
                nameof(itemId));
        }
    }
}
