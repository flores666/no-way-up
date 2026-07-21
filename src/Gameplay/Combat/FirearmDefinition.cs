using System;
using Godot;
using LineZero.Gameplay.Items;

namespace LineZero.Gameplay.Combat;

[GlobalClass]
public sealed partial class FirearmDefinition : Resource
{
    private string _id = string.Empty;
    private string _displayName = string.Empty;

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

    [Export]
    public ItemDefinition? AmmoItemDefinition { get; set; }

    [Export(PropertyHint.Range, "1,999,1,or_greater")]
    public int MagazineCapacity { get; set; } = 8;

    [Export(PropertyHint.Range, "1,9999,1,or_greater")]
    public int Damage { get; set; } = 25;

    [Export(PropertyHint.Range, "0.0,60.0,0.01,or_greater")]
    public double FireIntervalSeconds { get; set; } = 0.25;

    [Export(PropertyHint.Range, "0.01,60.0,0.01,or_greater")]
    public double ReloadDurationSeconds { get; set; } = 1.2;

    [Export(PropertyHint.Range, "1.0,10000.0,1.0,or_greater")]
    public float Range { get; set; } = 700.0f;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException(
                $"{nameof(FirearmDefinition)} at '{GetDisplayPath()}' requires a non-empty ID.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException(
                $"{nameof(FirearmDefinition)} '{Id}' requires a non-empty display name.");
        }

        ItemDefinition ammoItem = AmmoItemDefinition
            ?? throw new InvalidOperationException(
                $"{nameof(FirearmDefinition)} '{Id}' requires an ammunition item definition.");
        ammoItem.Validate();

        if (MagazineCapacity < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(FirearmDefinition)} '{Id}' requires a positive magazine capacity.");
        }

        if (Damage < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(FirearmDefinition)} '{Id}' requires positive damage.");
        }

        if (FireIntervalSeconds < 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(FirearmDefinition)} '{Id}' cannot have a negative fire interval.");
        }

        if (ReloadDurationSeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(FirearmDefinition)} '{Id}' requires a positive reload duration.");
        }

        if (Range <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(FirearmDefinition)} '{Id}' requires positive range.");
        }
    }

    private string GetDisplayPath()
    {
        return string.IsNullOrWhiteSpace(ResourcePath) ? "<unsaved resource>" : ResourcePath;
    }
}
