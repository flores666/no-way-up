using System.Threading.Tasks;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class InventoryFeatureTests : IFeatureTestSuite
{
    public string Id => "inventory";

    public string Description => "Stacking, removal, transfer, and transactional notifications";

    public Task RunAsync(FeatureTestContext context)
    {
        context.Run("stacking-respects-capacity", () =>
        {
            ItemDefinition scrap = TestDataFactory.CreateItem("scrap", maxStackSize: 3);
            InventoryModel inventory = new(capacity: 2);
            InventoryAddResult result = inventory.TryAdd(scrap, 8);

            TestAssert.Equal(6, result.AddedQuantity, "Inventory exceeded slot capacity.");
            TestAssert.Equal(2, result.RemainingQuantity, "Remaining quantity was incorrect.");
            TestAssert.Equal(6, inventory.CountByItemId("scrap"), "Stacked quantity was incorrect.");
            TestAssert.Equal(3, inventory.Slots[0].Quantity, "First stack was incorrect.");
            TestAssert.Equal(3, inventory.Slots[1].Quantity, "Second stack was incorrect.");
        });

        context.Run("remove-by-id-is-deterministic", () =>
        {
            ItemDefinition battery = TestDataFactory.CreateBattery(maxStackSize: 2);
            InventoryModel inventory = new(capacity: 3);
            inventory.TryAdd(battery, 5);
            int notifications = 0;
            inventory.Changed += () => notifications++;

            InventoryItemRemovalResult result = inventory.TryRemoveByItemId("battery", 3);

            TestAssert.Equal(3, result.RemovedQuantity, "Wrong quantity removed by ID.");
            TestAssert.Equal(2, result.RemainingItemQuantity, "Remaining item count was incorrect.");
            TestAssert.Equal(2, inventory.CountByItemId("battery"), "Inventory count was incorrect.");
            TestAssert.Equal(1, notifications, "Removal did not publish exactly one notification.");
        });

        context.Run("transfer-updates-both-models-after-consistency", () =>
        {
            ItemDefinition ammo = TestDataFactory.CreateItem("ammo", maxStackSize: 10);
            InventoryModel source = new(capacity: 1);
            InventoryModel destination = new(capacity: 1);
            source.TryAdd(ammo, 8);
            int sourceNotifications = 0;
            int destinationNotifications = 0;
            source.Changed += () =>
            {
                sourceNotifications++;
                TestAssert.Equal(3, source.CountByItemId("ammo"),
                    "Source notification observed an intermediate state.");
                TestAssert.Equal(5, destination.CountByItemId("ammo"),
                    "Source notification observed an incomplete destination.");
            };
            destination.Changed += () =>
            {
                destinationNotifications++;
                TestAssert.Equal(3, source.CountByItemId("ammo"),
                    "Destination notification observed an incomplete source.");
                TestAssert.Equal(5, destination.CountByItemId("ammo"),
                    "Destination notification observed an intermediate state.");
            };

            InventoryTransferResult result = source.TryTransferTo(destination, 0, 5);

            TestAssert.Equal(5, result.TransferredQuantity, "Transfer quantity was incorrect.");
            TestAssert.Equal(1, sourceNotifications, "Source notification count was incorrect.");
            TestAssert.Equal(1, destinationNotifications, "Destination notification count was incorrect.");
        });

        context.Run("failed-transfer-changes-nothing", () =>
        {
            ItemDefinition item = TestDataFactory.CreateItem("item", maxStackSize: 1);
            ItemDefinition blocker = TestDataFactory.CreateItem("blocker", maxStackSize: 1);
            InventoryModel source = new(capacity: 1);
            InventoryModel destination = new(capacity: 1);
            source.TryAdd(item, 1);
            destination.TryAdd(blocker, 1);
            int notifications = 0;
            source.Changed += () => notifications++;
            destination.Changed += () => notifications++;

            InventoryTransferResult result = source.TryTransferTo(destination, 0, 1);

            TestAssert.True(result.TransferredNothing, "Full destination accepted a transfer.");
            TestAssert.Equal(1, source.CountByItemId("item"), "Failed transfer changed source.");
            TestAssert.Equal(1, destination.CountByItemId("blocker"), "Failed transfer changed destination.");
            TestAssert.Equal(0, notifications, "Failed transfer published notifications.");
        });

        return Task.CompletedTask;
    }
}
