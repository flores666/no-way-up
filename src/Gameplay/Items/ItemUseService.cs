using System;
using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Inventory;

namespace LineZero.Gameplay.Items;

public sealed class ItemUseService
{
    private bool _isExecuting;

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

        if (_isExecuting)
        {
            return ItemUseResult.Failure("Cannot use items now.");
        }

        if (slotIndex < 0 || slotIndex >= inventory.Capacity)
        {
            return ItemUseResult.Failure("Select a valid inventory slot.");
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

        if (item.UseEffect is not HealingItemUseEffectDefinition healingEffect)
        {
            return ItemUseResult.Failure($"{item.DisplayName} cannot be used.");
        }

        healingEffect.Validate();
        if (actor is not IHealthOwner healthOwner)
        {
            return ItemUseResult.Failure("This actor cannot receive healing.");
        }

        HealthModel health = healthOwner.Health;
        if (health.IsDead)
        {
            return ItemUseResult.Failure("Medical items cannot be used after death.");
        }

        if (health.CurrentHealth == health.MaxHealth)
        {
            return ItemUseResult.Failure("Health is already full.");
        }

        if (!inventory.TryPrepareSingleItemRemovalFromSlot(
                slotIndex,
                item,
                out InventorySingleItemRemovalPlan inventoryPlan) ||
            !health.TryPrepareHealing(
                healingEffect.HealAmount,
                out HealthHealingPlan healthPlan))
        {
            return ItemUseResult.Failure("Health could not be restored.");
        }

        _isExecuting = true;
        try
        {
            if (!inventory.CanApply(inventoryPlan) || !health.CanApply(healthPlan))
            {
                throw new InvalidOperationException(
                    "Item-use transaction plans became invalid before commit.");
            }

            inventory.ApplyWithoutNotification(inventoryPlan);
            HealthChangeResult healing = health.ApplyWithoutNotification(healthPlan);

            inventory.PublishChanged();
            health.PublishHealing(healing);
            return ItemUseResult.Consumed(
                $"Restored {healing.AppliedAmount} health.",
                healing.AppliedAmount);
        }
        finally
        {
            _isExecuting = false;
        }
    }
}
