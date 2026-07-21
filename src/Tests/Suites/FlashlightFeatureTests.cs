using System.Threading.Tasks;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Inventory;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class FlashlightFeatureTests : IFeatureTestSuite
{
    public string Id => "flashlight";

    public string Description => "Charge, thresholds, depletion, and battery transaction";

    public Task RunAsync(FeatureTestContext context)
    {
        context.Run("drain-thresholds-and-depletion-are-crossing-based", () =>
        {
            FlashlightModel model = new(TestDataFactory.CreateFlashlightDefinition(), startOn: true);
            int changed = 0;
            int low = 0;
            int critical = 0;
            int depleted = 0;
            int powerChanges = 0;
            model.Changed += () => changed++;
            model.LowChargeReached += _ => low++;
            model.CriticalChargeReached += _ => critical++;
            model.Depleted += _ => depleted++;
            model.PowerStateChanged += _ => powerChanges++;

            model.Drain(75.0);
            model.Drain(15.0);
            model.Drain(10.0);
            model.Drain(1.0);

            TestAssert.Equal(3, changed, "Drain changed-event count was incorrect.");
            TestAssert.Equal(1, low, "Low threshold was not crossing-based.");
            TestAssert.Equal(1, critical, "Critical threshold was not crossing-based.");
            TestAssert.Equal(1, depleted, "Depletion was not exactly-once.");
            TestAssert.Equal(1, powerChanges, "Depletion did not switch power once.");
            TestAssert.True(model.IsDepleted && !model.IsOn,
                "Depleted flashlight remained on.");
        });

        context.Run("off-flashlight-does-not-drain", () =>
        {
            FlashlightModel model = new(TestDataFactory.CreateFlashlightDefinition(), startOn: false);
            double before = model.CurrentCharge;
            FlashlightChargeResult result = model.Drain(10.0);

            TestAssert.False(result.Changed, "Off flashlight reported drain change.");
            TestAssert.NearlyEqual(before, model.CurrentCharge, 1e-9,
                "Off flashlight lost charge.");
        });

        context.Run("battery-replacement-is-atomic-and-near-full-safe", () =>
        {
            FlashlightModel model = new(TestDataFactory.CreateFlashlightDefinition(), startOn: true);
            model.Drain(40.0);
            InventoryModel inventory = new(capacity: 2);
            inventory.TryAdd(TestDataFactory.CreateBattery(), 2);
            int inventoryChanges = 0;
            int flashlightChanges = 0;
            inventory.Changed += () => inventoryChanges++;
            model.Changed += () => flashlightChanges++;

            FlashlightBatteryService service = new();
            BatteryReplacementResult success = service.TryReplaceBattery(
                model,
                inventory,
                canReplace: true);
            BatteryReplacementResult duplicate = service.TryReplaceBattery(
                model,
                inventory,
                canReplace: true);

            TestAssert.True(success.Success && success.BatteryConsumed,
                "Valid battery replacement failed.");
            TestAssert.NearlyEqual(40.0, success.RestoredCharge, 1e-9,
                "Replacement restored the wrong useful charge.");
            TestAssert.Equal(1, inventory.CountByItemId("battery"),
                "Replacement did not consume exactly one battery.");
            TestAssert.Equal(1, inventoryChanges,
                "Replacement published the wrong inventory event count.");
            TestAssert.Equal(1, flashlightChanges,
                "Replacement published the wrong flashlight event count.");
            TestAssert.False(duplicate.Success || duplicate.BatteryConsumed,
                "Full-charge duplicate replacement changed state.");

            model.TryTurnOn();
            model.Drain(0.01);
            int batteryBefore = inventory.CountByItemId("battery");
            BatteryReplacementResult nearFull = service.TryReplaceBattery(
                model,
                inventory,
                canReplace: true);
            TestAssert.False(nearFull.Success || nearFull.BatteryConsumed,
                "Near-full replacement consumed a battery.");
            TestAssert.Equal(batteryBefore, inventory.CountByItemId("battery"),
                "Near-full replacement changed inventory.");
        });

        context.Run("subscriber-failure-does-not-break-transaction", () =>
        {
            FlashlightModel model = new(TestDataFactory.CreateFlashlightDefinition(), startOn: true);
            model.Drain(50.0);
            InventoryModel inventory = new(capacity: 1);
            inventory.TryAdd(TestDataFactory.CreateBattery(), 1);
            int healthyInventory = 0;
            int healthyFlashlight = 0;
            inventory.Changed += () => throw new System.InvalidOperationException("Expected.");
            inventory.Changed += () => healthyInventory++;
            model.Changed += () => throw new System.InvalidOperationException("Expected.");
            model.Changed += () => healthyFlashlight++;

            BatteryReplacementResult result = new FlashlightBatteryService().TryReplaceBattery(
                model,
                inventory,
                canReplace: true);

            TestAssert.True(result.Success, "Subscriber failure changed transaction result.");
            TestAssert.Equal(0, inventory.CountByItemId("battery"),
                "Subscriber failure duplicated or prevented consumption.");
            TestAssert.NearlyEqual(100.0, model.CurrentCharge, 1e-9,
                "Subscriber failure prevented charge restoration.");
            TestAssert.Equal(1, healthyInventory,
                "Failing inventory subscriber blocked a healthy subscriber.");
            TestAssert.Equal(1, healthyFlashlight,
                "Failing flashlight subscriber blocked a healthy subscriber.");
        });

        return Task.CompletedTask;
    }
}
