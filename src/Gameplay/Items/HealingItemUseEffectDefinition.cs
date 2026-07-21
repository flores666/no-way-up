using System;
using Godot;

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

    private string GetDisplayPath()
    {
        return string.IsNullOrWhiteSpace(ResourcePath) ? "<unsaved resource>" : ResourcePath;
    }
}
