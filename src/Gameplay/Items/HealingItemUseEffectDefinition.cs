using System;
using Godot;
using LineZero.Gameplay.Health;

namespace LineZero.Gameplay.Items;

[GlobalClass]
public sealed partial class HealingItemUseEffectDefinition : ItemUseEffectDefinition
{
    [Export(PropertyHint.Range, "1,9999,1,or_greater")]
    public int HealAmount { get; set; } = 35;

    public override void Validate()
    {
        if (HealAmount < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(HealingItemUseEffectDefinition)} at '{GetDisplayPath()}' " +
                "requires a positive healing amount.");
        }
    }

    public override ItemUseResult CanUse(ItemUseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Validate();

        if (context.Actor is not IHealthOwner healthOwner)
        {
            return ItemUseResult.Failure("This actor cannot receive healing.");
        }

        if (healthOwner.Health.IsDead)
        {
            return ItemUseResult.Failure("Medical items cannot be used after death.");
        }

        if (healthOwner.Health.CurrentHealth == healthOwner.Health.MaxHealth)
        {
            return ItemUseResult.Failure("Health is already full.");
        }

        return ItemUseResult.Allowed("Medical item is ready to use.");
    }

    public override ItemUseResult Apply(ItemUseContext context)
    {
        ItemUseResult eligibility = CanUse(context);
        if (!eligibility.Success)
        {
            return eligibility;
        }

        IHealthOwner healthOwner = (IHealthOwner)context.Actor;
        HealthChangeResult change = healthOwner.Health.ApplyHealing(HealAmount);
        if (!change.Changed)
        {
            return ItemUseResult.Failure("Health could not be restored.");
        }

        return ItemUseResult.EffectApplied(
            $"Restored {change.AppliedAmount} health.",
            change.AppliedAmount);
    }

    private string GetDisplayPath()
    {
        return string.IsNullOrWhiteSpace(ResourcePath) ? "<unsaved resource>" : ResourcePath;
    }
}
