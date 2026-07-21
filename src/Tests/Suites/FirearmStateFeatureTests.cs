using System;
using System.Threading.Tasks;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Inventory;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class FirearmStateFeatureTests : IFeatureTestSuite
{
    public string Id => "firearm-state";

    public string Description =>
        "Magazine, safe events, transactional reload, cancellation, and ammunition conservation";

    public Task RunAsync(FeatureTestContext context)
    {
        context.Run("successful-shot-consumes-one-round", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 3);
            int changed = 0;
            state.Changed += () => changed++;

            FirearmShotResult result = state.TryConsumeRound();

            TestAssert.True(result.Success && result.RoundConsumed,
                "Valid firearm shot was rejected.");
            TestAssert.Equal(3, result.MagazineAmmoBefore,
                "Shot reported wrong pre-shot ammunition.");
            TestAssert.Equal(2, state.CurrentMagazineAmmo,
                "Shot did not consume exactly one round.");
            TestAssert.Equal(1, changed, "Shot state-change count was incorrect.");
        });

        context.Run("empty-and-reloading-shots-change-nothing", () =>
        {
            FirearmState empty = new(TestDataFactory.CreateFirearmDefinition(), 0);
            FirearmShotResult emptyResult = empty.TryConsumeRound();
            TestAssert.Equal(FirearmShotStatus.EmptyMagazine, emptyResult.Status,
                "Empty magazine returned the wrong status.");
            TestAssert.Equal(0, empty.CurrentMagazineAmmo,
                "Empty-magazine input changed ammunition.");

            FirearmState reloading = new(TestDataFactory.CreateFirearmDefinition(), 2);
            TestAssert.Equal(
                ReloadStatus.Started,
                reloading.TryBeginReload(6).Status,
                "Reload could not start.");
            FirearmShotResult reloadResult = reloading.TryConsumeRound();
            TestAssert.Equal(FirearmShotStatus.Reloading, reloadResult.Status,
                "Firing while reloading returned the wrong status.");
            TestAssert.Equal(2, reloading.CurrentMagazineAmmo,
                "Firing while reloading consumed ammunition.");
        });

        context.Run("firearm-subscriber-failure-does-not-block-later-subscribers", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 2);
            int healthyNotifications = 0;
            state.Changed += () => throw new InvalidOperationException("Expected.");
            state.Changed += () => healthyNotifications++;

            FirearmShotResult result = state.TryConsumeRound();

            TestAssert.True(result.Success, "Subscriber failure rejected a valid shot.");
            TestAssert.Equal(1, state.CurrentMagazineAmmo,
                "Subscriber failure repeated or reverted firearm mutation.");
            TestAssert.Equal(1, healthyNotifications,
                "A throwing firearm subscriber blocked later subscribers.");
        });


        context.Run("rapid-reload-start-does-not-duplicate-state", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 3);
            int changed = 0;
            state.Changed += () => changed++;

            ReloadResult first = state.TryBeginReload(5);
            ReloadResult second = state.TryBeginReload(5);

            TestAssert.Equal(ReloadStatus.Started, first.Status,
                "First reload input did not start reload.");
            TestAssert.Equal(ReloadStatus.AlreadyReloading, second.Status,
                "Rapid duplicate reload input was not rejected.");
            TestAssert.Equal(1, changed,
                "Rapid reload input published duplicate state changes.");
            TestAssert.Equal(3, state.CurrentMagazineAmmo,
                "Rapid reload input changed magazine ammunition.");
        });

        context.Run("reload-full-magazine-changes-nothing", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 8);
            InventoryModel inventory = CreateAmmoInventory(5);
            int totalBefore = TotalAmmo(state, inventory);
            int inventoryNotifications = 0;
            int firearmNotifications = 0;
            inventory.Changed += () => inventoryNotifications++;
            state.Changed += () => firearmNotifications++;

            ReloadResult result = new FirearmReloadService().TryCompleteReload(
                state,
                inventory,
                "pistol_ammo");

            TestAssert.Equal(ReloadStatus.MagazineFull, result.Status,
                "Full magazine returned the wrong reload status.");
            TestAssert.Equal(totalBefore, TotalAmmo(state, inventory),
                "Full-magazine reload changed ammunition.");
            TestAssert.Equal(0, inventoryNotifications,
                "Full-magazine reload published an inventory change.");
            TestAssert.Equal(0, firearmNotifications,
                "Full-magazine reload published a firearm change.");
        });

        context.Run("reload-without-reserve-changes-nothing", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 2);
            state.TryBeginReload(1);
            InventoryModel inventory = new(2);
            int totalBefore = TotalAmmo(state, inventory);
            int inventoryNotifications = 0;
            int firearmNotifications = 0;
            inventory.Changed += () => inventoryNotifications++;
            state.Changed += () => firearmNotifications++;

            ReloadResult result = new FirearmReloadService().TryCompleteReload(
                state,
                inventory,
                "pistol_ammo");

            TestAssert.Equal(ReloadStatus.NoReserveAmmo, result.Status,
                "No-reserve reload returned the wrong status.");
            TestAssert.Equal(totalBefore, TotalAmmo(state, inventory),
                "No-reserve reload changed ammunition.");
            TestAssert.True(state.IsReloading,
                "Rejected transaction mutated firearm reload state.");
            TestAssert.Equal(0, inventoryNotifications,
                "No-reserve reload published an inventory change.");
            TestAssert.Equal(0, firearmNotifications,
                "No-reserve reload published a firearm change.");
        });

        context.Run("partial-reload-is-transactional-and-conserves-ammunition", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 2);
            InventoryModel inventory = CreateAmmoInventory(3);
            state.TryBeginReload(3);
            int totalBefore = TotalAmmo(state, inventory);
            int inventoryNotifications = 0;
            int firearmNotifications = 0;
            inventory.Changed += () => inventoryNotifications++;
            state.Changed += () => firearmNotifications++;

            ReloadResult result = new FirearmReloadService().TryCompleteReload(
                state,
                inventory,
                "pistol_ammo");

            TestAssert.Equal(ReloadStatus.Completed, result.Status,
                "Partial reload did not complete.");
            TestAssert.Equal(3, result.LoadedRounds,
                "Partial reload loaded the wrong number of rounds.");
            TestAssert.Equal(5, state.CurrentMagazineAmmo,
                "Partial reload produced the wrong magazine count.");
            TestAssert.Equal(0, inventory.CountByItemId("pistol_ammo"),
                "Partial reload did not consume exact reserve ammunition.");
            TestAssert.Equal(totalBefore, TotalAmmo(state, inventory),
                "Partial reload did not conserve total ammunition.");
            TestAssert.Equal(1, inventoryNotifications,
                "Reload published the wrong inventory notification count.");
            TestAssert.Equal(1, firearmNotifications,
                "Reload published the wrong firearm notification count.");
        });

        context.Run("reload-cancellation-consumes-nothing", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 3);
            InventoryModel inventory = CreateAmmoInventory(5);
            state.TryBeginReload(5);
            int totalBefore = TotalAmmo(state, inventory);
            ReloadResult canceled = state.CancelReload();
            ReloadResult completion = new FirearmReloadService().TryCompleteReload(
                state,
                inventory,
                "pistol_ammo");

            TestAssert.Equal(ReloadStatus.Canceled, canceled.Status,
                "Reload cancellation returned the wrong status.");
            TestAssert.Equal(ReloadStatus.NotReloading, completion.Status,
                "Canceled reload was later completed.");
            TestAssert.Equal(totalBefore, TotalAmmo(state, inventory),
                "Canceled reload changed ammunition.");
        });

        context.Run("reload-subscriber-failures-and-reentrancy-do-not-duplicate-ammo", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 3);
            InventoryModel inventory = CreateAmmoInventory(5);
            FirearmReloadService service = new();
            state.TryBeginReload(5);
            int totalBefore = TotalAmmo(state, inventory);
            int healthyInventoryNotifications = 0;
            int healthyFirearmNotifications = 0;
            ReloadResult? reentrantResult = null;

            inventory.Changed += () => throw new InvalidOperationException("Expected.");
            inventory.Changed += () =>
            {
                healthyInventoryNotifications++;
                reentrantResult = service.TryCompleteReload(
                    state,
                    inventory,
                    "pistol_ammo");
            };
            state.Changed += () => throw new InvalidOperationException("Expected.");
            state.Changed += () => healthyFirearmNotifications++;

            ReloadResult result = service.TryCompleteReload(
                state,
                inventory,
                "pistol_ammo");

            TestAssert.Equal(ReloadStatus.Completed, result.Status,
                "Valid reload failed after subscriber exceptions.");
            TestAssert.True(reentrantResult is not null,
                "Notification-time reload reentrancy was not observed.");
            TestAssert.Equal(ReloadStatus.AlreadyReloading, reentrantResult!.Status,
                "Notification-time reload reentrancy was not rejected.");
            TestAssert.Equal(8, state.CurrentMagazineAmmo,
                "Reload did not clamp to magazine capacity.");
            TestAssert.Equal(0, inventory.CountByItemId("pistol_ammo"),
                "Reload consumed reserve ammunition incorrectly.");
            TestAssert.Equal(totalBefore, TotalAmmo(state, inventory),
                "Reload subscriber failures duplicated or lost ammunition.");
            TestAssert.Equal(1, healthyInventoryNotifications,
                "Throwing inventory subscriber blocked later notification.");
            TestAssert.Equal(1, healthyFirearmNotifications,
                "Throwing firearm subscriber blocked later notification.");
        });

        return Task.CompletedTask;
    }

    private static InventoryModel CreateAmmoInventory(int quantity)
    {
        InventoryModel inventory = new(4);
        if (quantity > 0)
        {
            inventory.TryAdd(
                TestDataFactory.CreateItem("pistol_ammo", maxStackSize: 3),
                quantity);
        }

        return inventory;
    }

    private static int TotalAmmo(FirearmState state, InventoryModel inventory)
    {
        return state.CurrentMagazineAmmo + inventory.CountByItemId("pistol_ammo");
    }
}
