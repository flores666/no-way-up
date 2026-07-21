using System;
using Godot;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;

namespace LineZero.Validation;

public sealed partial class FlashlightBatteryServiceValidation : Node
{
    private const double Tolerance = 0.000000001;

    public override void _Ready()
    {
        try
        {
            ValidateMissingBattery();
            ValidateNearFullReplacement();
            ValidateIneligiblePlayer();
            ValidateSubscriberFailureIsolation();
            GD.Print("FlashlightBatteryService validation passed.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"FlashlightBatteryService validation failed: {exception}");
            GetTree().Quit(1);
        }
    }

    private static void ValidateMissingBattery()
    {
        FlashlightModel flashlight = CreateDrainedFlashlight(drainAmount: 40.0);
        InventoryModel inventory = new(capacity: 2);
        FlashlightBatteryService service = new();
        int inventoryNotifications = 0;
        int flashlightNotifications = 0;

        inventory.Changed += () => inventoryNotifications++;
        flashlight.Changed += () => flashlightNotifications++;

        double chargeBefore = flashlight.CurrentCharge;
        BatteryReplacementResult result = service.TryReplaceBattery(
            flashlight,
            inventory,
            canReplace: true);

        Require(!result.Success && !result.BatteryConsumed,
            "Missing-battery replacement must fail without consumption.");
        Require(result.Message == FlashlightBatteryService.NoSpareBatteriesMessage,
            "Missing-battery replacement returned the wrong message.");
        Require(AreEqual(flashlight.CurrentCharge, chargeBefore),
            "Missing-battery replacement changed flashlight charge.");
        Require(inventory.CountByItemId(FlashlightDefinition.RequiredBatteryItemId) == 0,
            "Missing-battery replacement changed inventory.");
        Require(inventoryNotifications == 0 && flashlightNotifications == 0,
            "Missing-battery replacement published notifications.");
    }

    private static void ValidateNearFullReplacement()
    {
        FlashlightModel flashlight = CreateDrainedFlashlight(drainAmount: 0.01);
        InventoryModel inventory = CreateInventoryWithBatteries(quantity: 1);
        FlashlightBatteryService service = new();
        int inventoryNotifications = 0;
        int flashlightNotifications = 0;

        inventory.Changed += () => inventoryNotifications++;
        flashlight.Changed += () => flashlightNotifications++;

        double chargeBefore = flashlight.CurrentCharge;
        BatteryReplacementResult result = service.TryReplaceBattery(
            flashlight,
            inventory,
            canReplace: true);

        Require(!result.Success && !result.BatteryConsumed,
            "Near-full replacement must not consume a battery.");
        Require(result.Message == FlashlightBatteryService.AlreadyFullMessage,
            "Near-full replacement returned the wrong message.");
        Require(AreEqual(flashlight.CurrentCharge, chargeBefore),
            "Near-full replacement changed flashlight charge.");
        Require(inventory.CountByItemId(FlashlightDefinition.RequiredBatteryItemId) == 1,
            "Near-full replacement consumed a battery.");
        Require(inventoryNotifications == 0 && flashlightNotifications == 0,
            "Near-full replacement published notifications.");
    }

    private static void ValidateIneligiblePlayer()
    {
        FlashlightModel flashlight = CreateDrainedFlashlight(drainAmount: 40.0);
        InventoryModel inventory = CreateInventoryWithBatteries(quantity: 1);
        FlashlightBatteryService service = new();
        double chargeBefore = flashlight.CurrentCharge;

        BatteryReplacementResult result = service.TryReplaceBattery(
            flashlight,
            inventory,
            canReplace: false);

        Require(!result.Success && !result.BatteryConsumed,
            "Ineligible replacement must fail without consumption.");
        Require(result.Message == FlashlightBatteryService.CannotReplaceNowMessage,
            "Ineligible replacement returned the wrong message.");
        Require(AreEqual(flashlight.CurrentCharge, chargeBefore),
            "Ineligible replacement changed flashlight charge.");
        Require(inventory.CountByItemId(FlashlightDefinition.RequiredBatteryItemId) == 1,
            "Ineligible replacement consumed a battery.");
    }

    private static void ValidateSubscriberFailureIsolation()
    {
        FlashlightModel flashlight = CreateDrainedFlashlight(drainAmount: 40.0);
        InventoryModel inventory = CreateInventoryWithBatteries(quantity: 2);
        FlashlightBatteryService service = new();
        int healthyInventorySubscribers = 0;
        int healthyFlashlightSubscribers = 0;
        BatteryReplacementResult? reentrantResult = null;

        inventory.Changed += ThrowExpectedSubscriberFailure;
        inventory.Changed += () => healthyInventorySubscribers++;
        flashlight.Changed += ThrowExpectedSubscriberFailure;
        flashlight.Changed += () =>
        {
            reentrantResult = service.TryReplaceBattery(
                flashlight,
                inventory,
                canReplace: true);
        };
        flashlight.Changed += () => healthyFlashlightSubscribers++;

        BatteryReplacementResult result = service.TryReplaceBattery(
            flashlight,
            inventory,
            canReplace: true);

        Require(result.Success && result.BatteryConsumed,
            "Subscriber failure changed the successful replacement result.");
        Require(AreEqual(result.PreviousCharge, 60.0) &&
                AreEqual(result.CurrentCharge, 100.0) &&
                AreEqual(result.RestoredCharge, 40.0),
            "Successful replacement did not restore the calculated charge exactly once.");
        Require(AreEqual(flashlight.CurrentCharge, 100.0),
            "Subscriber failure prevented flashlight restoration.");
        Require(inventory.CountByItemId(FlashlightDefinition.RequiredBatteryItemId) == 1,
            "Subscriber failure changed exact battery consumption.");
        Require(healthyInventorySubscribers == 1 &&
                healthyFlashlightSubscribers == 1,
            "One failing subscriber stopped another required notification.");
        Require(reentrantResult.HasValue &&
                !reentrantResult.Value.Success &&
                !reentrantResult.Value.BatteryConsumed &&
                reentrantResult.Value.Message ==
                    FlashlightBatteryService.CannotReplaceNowMessage,
            "A notification-time reentrant request duplicated replacement.");

        BatteryReplacementResult duplicate = service.TryReplaceBattery(
            flashlight,
            inventory,
            canReplace: true);
        Require(!duplicate.Success && !duplicate.BatteryConsumed,
            "Full-charge duplicate replacement changed state.");
        Require(inventory.CountByItemId(FlashlightDefinition.RequiredBatteryItemId) == 1,
            "Full-charge duplicate replacement consumed another battery.");
        Require(healthyInventorySubscribers == 1 &&
                healthyFlashlightSubscribers == 1,
            "Rejected duplicate replacement published notifications.");
    }

    private static FlashlightModel CreateDrainedFlashlight(double drainAmount)
    {
        FlashlightModel flashlight = new(CreateDefinition(), startOn: true);
        FlashlightChargeResult result = flashlight.Drain(drainAmount);
        Require(result.Applied > 0.0, "Validation setup could not drain flashlight charge.");
        return flashlight;
    }

    private static InventoryModel CreateInventoryWithBatteries(int quantity)
    {
        InventoryModel inventory = new(capacity: 2);
        InventoryAddResult result = inventory.TryAdd(CreateBattery(), quantity);
        Require(result.AddedQuantity == quantity,
            "Validation setup could not add the requested batteries.");
        return inventory;
    }

    private static FlashlightDefinition CreateDefinition()
    {
        return new FlashlightDefinition
        {
            Id = "validation_flashlight",
            DisplayName = "Validation Flashlight",
            MaximumCharge = 100.0,
            DrainPerSecond = 1.0,
            LowChargeThreshold = 25.0,
            CriticalChargeThreshold = 10.0,
            BatteryItemDefinition = CreateBattery(),
            ChargeRestoredPerBattery = 100.0,
        };
    }

    private static ItemDefinition CreateBattery()
    {
        return new ItemDefinition
        {
            Id = FlashlightDefinition.RequiredBatteryItemId,
            DisplayName = "Battery",
            Description = "Validation battery.",
            MaxStackSize = 5,
        };
    }

    private static bool AreEqual(double first, double second)
    {
        return Math.Abs(first - second) <= Tolerance;
    }

    private static void ThrowExpectedSubscriberFailure()
    {
        throw new InvalidOperationException(
            "Expected validation subscriber failure.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
