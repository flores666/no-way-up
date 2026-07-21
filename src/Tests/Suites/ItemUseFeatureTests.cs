using System;
using System.Threading.Tasks;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Items;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class ItemUseFeatureTests : IFeatureTestSuite
{
    public string Id => "item-use";

    public string Description =>
        "Transactional medkit use, eligibility, exact healing, and safe notifications";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("successful-medkit-use-consumes-and-heals-once", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            actor.Health.ApplyDamage(new DamageInfo(40));
            actor.Inventory.TryAdd(CreateMedkit(maxStackSize: 2), 2);
            int inventoryNotifications = 0;
            int healthNotifications = 0;
            actor.Inventory.Changed += () => inventoryNotifications++;
            actor.Health.Changed += _ => healthNotifications++;

            ItemUseResult result = new ItemUseService().TryUseFromSlot(
                actor,
                actor.Inventory,
                0);

            TestAssert.True(result.Success && result.ItemConsumed,
                "Successful healing did not consume exactly one item.");
            TestAssert.Equal(35, result.AppliedAmount.GetValueOrDefault(),
                "Healing effect applied the wrong amount.");
            TestAssert.Equal(95, actor.Health.CurrentHealth,
                "Healing effect changed health incorrectly.");
            TestAssert.Equal(1, actor.Inventory.CountByItemId("medkit"),
                "Healing item consumption was not exactly one.");
            TestAssert.Equal(1, inventoryNotifications,
                "Successful item use published the wrong inventory notification count.");
            TestAssert.Equal(1, healthNotifications,
                "Successful item use published the wrong health notification count.");

            await context.DisposeNodeAsync(actor);
        });

        await context.RunAsync("medkit-at-full-health-changes-nothing", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            actor.Inventory.TryAdd(CreateMedkit(), 1);
            int inventoryNotifications = 0;
            int healthNotifications = 0;
            actor.Inventory.Changed += () => inventoryNotifications++;
            actor.Health.Changed += _ => healthNotifications++;

            ItemUseResult result = new ItemUseService().TryUseFromSlot(
                actor,
                actor.Inventory,
                0);

            TestAssert.False(result.Success, "Full-health actor used a medkit.");
            TestAssert.Equal(1, actor.Inventory.CountByItemId("medkit"),
                "Full-health use consumed a medkit.");
            TestAssert.Equal(100, actor.Health.CurrentHealth,
                "Full-health use changed health.");
            TestAssert.Equal(0, inventoryNotifications,
                "Full-health use published an inventory change.");
            TestAssert.Equal(0, healthNotifications,
                "Full-health use published a health change.");

            await context.DisposeNodeAsync(actor);
        });

        await context.RunAsync("medkit-while-dead-changes-nothing", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            actor.Inventory.TryAdd(CreateMedkit(), 1);
            actor.Health.ApplyDamage(new DamageInfo(100));
            int inventoryNotifications = 0;
            int healthNotifications = 0;
            actor.Inventory.Changed += () => inventoryNotifications++;
            actor.Health.Changed += _ => healthNotifications++;

            ItemUseResult result = new ItemUseService().TryUseFromSlot(
                actor,
                actor.Inventory,
                0);

            TestAssert.False(result.Success, "Dead actor used a medkit.");
            TestAssert.Equal(1, actor.Inventory.CountByItemId("medkit"),
                "Dead actor consumed a medkit.");
            TestAssert.Equal(0, actor.Health.CurrentHealth,
                "Dead actor was healed.");
            TestAssert.Equal(0, inventoryNotifications,
                "Dead item use published an inventory change.");
            TestAssert.Equal(0, healthNotifications,
                "Dead item use published a health change.");

            await context.DisposeNodeAsync(actor);
        });

        await context.RunAsync("invalid-and-empty-slots-change-nothing", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            actor.Health.ApplyDamage(new DamageInfo(20));
            ItemUseService service = new();

            ItemUseResult invalid = service.TryUseFromSlot(
                actor,
                actor.Inventory,
                99);
            ItemUseResult empty = service.TryUseFromSlot(
                actor,
                actor.Inventory,
                0);

            TestAssert.False(invalid.Success, "Invalid slot was accepted.");
            TestAssert.False(empty.Success, "Empty slot was accepted.");
            TestAssert.Equal(80, actor.Health.CurrentHealth,
                "Invalid or empty item use changed health.");
            TestAssert.Equal(0, actor.Inventory.CountByItemId("medkit"),
                "Invalid or empty item use changed inventory.");

            await context.DisposeNodeAsync(actor);
        });

        await context.RunAsync("final-medkit-in-stack-clears-slot", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            actor.Health.ApplyDamage(new DamageInfo(20));
            actor.Inventory.TryAdd(CreateMedkit(), 1);

            ItemUseResult result = new ItemUseService().TryUseFromSlot(
                actor,
                actor.Inventory,
                0);

            TestAssert.True(result.Success && result.ItemConsumed,
                "Final medkit was not used successfully.");
            TestAssert.Equal(0, actor.Inventory.CountByItemId("medkit"),
                "Final medkit remained in inventory.");
            TestAssert.True(actor.Inventory.Slots[0].IsEmpty,
                "Final medkit did not clear its inventory slot.");
            TestAssert.Equal(100, actor.Health.CurrentHealth,
                "Final medkit did not clamp healing to maximum.");

            await context.DisposeNodeAsync(actor);
        });

        await context.RunAsync("subscriber-failures-and-reentrancy-preserve-transaction", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            actor.Health.ApplyDamage(new DamageInfo(70));
            actor.Inventory.TryAdd(CreateMedkit(maxStackSize: 2), 2);
            ItemUseService service = new();
            int healthyInventoryNotifications = 0;
            int healthyHealthNotifications = 0;
            ItemUseResult? reentrantResult = null;

            actor.Inventory.Changed += () => throw new InvalidOperationException("Expected.");
            actor.Inventory.Changed += () =>
            {
                healthyInventoryNotifications++;
                reentrantResult = service.TryUseFromSlot(actor, actor.Inventory, 0);
            };
            actor.Health.Changed += _ => throw new InvalidOperationException("Expected.");
            actor.Health.Changed += _ => healthyHealthNotifications++;

            ItemUseResult result = service.TryUseFromSlot(actor, actor.Inventory, 0);

            TestAssert.True(result.Success && result.ItemConsumed,
                "Subscriber failure rejected valid medkit use.");
            TestAssert.False(reentrantResult?.Success ?? true,
                "Notification-time reentrant medkit use was accepted.");
            TestAssert.Equal(65, actor.Health.CurrentHealth,
                "Subscriber failure duplicated or reverted healing.");
            TestAssert.Equal(1, actor.Inventory.CountByItemId("medkit"),
                "Subscriber failure duplicated medkit consumption.");
            TestAssert.Equal(1, healthyInventoryNotifications,
                "Throwing inventory subscriber blocked later notification.");
            TestAssert.Equal(1, healthyHealthNotifications,
                "Throwing health subscriber blocked later notification.");

            await context.DisposeNodeAsync(actor);
        });

        await context.RunAsync("ordinary-items-have-no-use-path", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            actor.Inventory.TryAdd(TestDataFactory.CreateItem("scrap"), 1);

            ItemUseResult result = new ItemUseService().TryUseFromSlot(
                actor,
                actor.Inventory,
                0);

            TestAssert.False(result.Success, "Ordinary inventory item was usable.");
            TestAssert.Equal(1, actor.Inventory.CountByItemId("scrap"),
                "Ordinary item use changed inventory.");

            await context.DisposeNodeAsync(actor);
        });
    }

    private static ItemDefinition CreateMedkit(int maxStackSize = 5)
    {
        return TestDataFactory.CreateItem(
            "medkit",
            maxStackSize,
            new HealingItemUseEffectDefinition { HealAmount = 35 });
    }
}
