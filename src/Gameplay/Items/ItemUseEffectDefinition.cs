using Godot;

namespace LineZero.Gameplay.Items;

[GlobalClass]
public abstract partial class ItemUseEffectDefinition : Resource
{
    public abstract void Validate();

    public abstract ItemUseResult CanUse(ItemUseContext context);

    public abstract ItemUseResult Apply(ItemUseContext context);
}
