using System;
using Godot;
using LineZero.Gameplay.Inventory;

namespace LineZero.Gameplay.Items;

public sealed class ItemUseService
{
    public ItemUseResult TryUseFromSlot(
        Node actor,
        InventoryModel inventory,
        int slotIndex)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(inventory);

        if (!GodotObject.IsInstanceValid(actor))
        {
            throw new ArgumentException(
                "Item-use actor must be a valid Godot node.",
                nameof(actor));
        }

        if (slotIndex < 0 || slotIndex >= inventory.Capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex),
                $"Slot index must be between 0 and {inventory.Capacity - 1}.");
        }

        InventorySlot sourceSlot = inventory.Slots[slotIndex];
        if (sourceSlot.IsEmpty)
        {
            return ItemUseResult.Failure("Select a non-empty slot.");
        }

        ItemDefinition item = sourceSlot.Item
            ?? throw new InvalidOperationException(
                "A populated inventory slot must reference an item definition.");
        item.Validate();

        ItemUseEffectDefinition? useEffect = item.UseEffect;
        if (useEffect is null)
        {
            return ItemUseResult.Failure($"{item.DisplayName} cannot be used.");
        }

        int sourceQuantityBeforeUse = sourceSlot.Quantity;
        ItemUseContext context = new(actor, inventory, slotIndex);
        ItemUseResult eligibility = useEffect.CanUse(context);
        EnsureEffectDidNotConsumeItem(eligibility, useEffect);
        if (!eligibility.Success)
        {
            return eligibility;
        }

        ItemUseResult effectResult = useEffect.Apply(context);
        EnsureEffectDidNotConsumeItem(effectResult, useEffect);
        if (!effectResult.Success)
        {
            return effectResult;
        }

        InventorySlot currentSlot = inventory.Slots[slotIndex];
        if (currentSlot.IsEmpty ||
            !ReferenceEquals(currentSlot.Item, item) ||
            currentSlot.Quantity != sourceQuantityBeforeUse)
        {
            throw new InvalidOperationException(
                $"{useEffect.GetType().Name} changed inventory state during item use.");
        }

        InventoryRemoveResult removal = inventory.TryRemoveFromSlot(slotIndex, 1);
        if (removal.RemovedQuantity != 1)
        {
            throw new InvalidOperationException(
                "A successful item effect must consume exactly one source item.");
        }

        return ItemUseResult.Consumed(
            effectResult.Message,
            effectResult.AppliedAmount);
    }

    private static void EnsureEffectDidNotConsumeItem(
        ItemUseResult result,
        ItemUseEffectDefinition useEffect)
    {
        if (result.ItemConsumed)
        {
            throw new InvalidOperationException(
                $"{useEffect.GetType().Name} must not consume inventory items directly.");
        }
    }
}
