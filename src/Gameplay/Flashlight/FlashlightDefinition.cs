using System;
using Godot;
using LineZero.Gameplay.Items;

namespace LineZero.Gameplay.Flashlight;

[GlobalClass]
public sealed partial class FlashlightDefinition : Resource
{
    public const string RequiredBatteryItemId = "battery";

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

    [Export(PropertyHint.Range, "0.01,10000.0,0.01,or_greater")]
    public double MaximumCharge { get; set; } = 100.0;

    [Export(PropertyHint.Range, "0.01,1000.0,0.01,or_greater")]
    public double DrainPerSecond { get; set; } = 1.0;

    [Export(PropertyHint.Range, "0.0,10000.0,0.01,or_greater")]
    public double LowChargeThreshold { get; set; } = 25.0;

    [Export(PropertyHint.Range, "0.0,10000.0,0.01,or_greater")]
    public double CriticalChargeThreshold { get; set; } = 10.0;

    [Export]
    public ItemDefinition? BatteryItemDefinition { get; set; }

    [Export(PropertyHint.Range, "0.01,10000.0,0.01,or_greater")]
    public double ChargeRestoredPerBattery { get; set; } = 100.0;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException(
                $"{nameof(FlashlightDefinition)} at '{GetDisplayPath()}' requires a non-empty ID.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException(
                $"{nameof(FlashlightDefinition)} '{Id}' requires a non-empty display name.");
        }

        ValidatePositiveFinite(MaximumCharge, nameof(MaximumCharge));
        ValidatePositiveFinite(DrainPerSecond, nameof(DrainPerSecond));
        ValidatePositiveFinite(ChargeRestoredPerBattery, nameof(ChargeRestoredPerBattery));

        if (!double.IsFinite(CriticalChargeThreshold) ||
            !double.IsFinite(LowChargeThreshold) ||
            CriticalChargeThreshold < 0.0 ||
            CriticalChargeThreshold >= LowChargeThreshold ||
            LowChargeThreshold >= MaximumCharge)
        {
            throw new InvalidOperationException(
                $"{nameof(FlashlightDefinition)} '{Id}' requires " +
                $"0 <= critical < low < maximum charge.");
        }

        ItemDefinition batteryItem = BatteryItemDefinition
            ?? throw new InvalidOperationException(
                $"{nameof(FlashlightDefinition)} '{Id}' requires a battery item definition.");
        batteryItem.Validate();
        if (!string.Equals(
                batteryItem.Id,
                RequiredBatteryItemId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{nameof(FlashlightDefinition)} '{Id}' requires the stable battery item ID " +
                $"'{RequiredBatteryItemId}'.");
        }
    }

    private static void ValidatePositiveFinite(double value, string propertyName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new InvalidOperationException(
                $"Flashlight property '{propertyName}' must be finite and positive.");
        }
    }

    private string GetDisplayPath()
    {
        return string.IsNullOrWhiteSpace(ResourcePath) ? "<unsaved resource>" : ResourcePath;
    }
}
