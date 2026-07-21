using System;
using Godot;

namespace LineZero.Gameplay.Items;

[GlobalClass]
public sealed partial class ItemDefinition : Resource
{
    private string _id = string.Empty;
    private string _displayName = string.Empty;
    private string _description = string.Empty;

    [Export]
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    [Export]
    public string DisplayName
    {
        get => _displayName;
        set => _displayName = value ?? string.Empty;
    }

    [Export(PropertyHint.MultilineText)]
    public string Description
    {
        get => _description;
        set => _description = value ?? string.Empty;
    }

    [Export(PropertyHint.Range, "1,999,1,or_greater")]
    public int MaxStackSize { get; set; } = 1;

    [Export]
    public ItemUseEffectDefinition? UseEffect { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException(
                $"{nameof(ItemDefinition)} at '{GetDisplayPath()}' requires a non-empty ID.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException(
                $"{nameof(ItemDefinition)} '{Id}' requires a non-empty display name.");
        }

        if (MaxStackSize < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(ItemDefinition)} '{Id}' requires a maximum stack size of at least one.");
        }

        UseEffect?.Validate();
    }

    private string GetDisplayPath()
    {
        return string.IsNullOrWhiteSpace(ResourcePath) ? "<unsaved resource>" : ResourcePath;
    }
}
