using System.Threading.Tasks;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class ItemUseFeatureTests : IFeatureTestSuite
{
    public string Id => "item-use";

    public string Description => "Item eligibility, healing, and exact inventory consumption";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("healing-item-consumes-on-success-only", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            actor.Health.ApplyDamage(new DamageInfo(40));

            HealingItemUseEffectDefinition effect = new() { HealAmount = 35 };
            ItemDefinition medkit = TestDataFactory.CreateItem(
                "medkit",
                maxStackSize: 2,
                useEffect: effect);
            actor.Inventory.TryAdd(medkit, 2);
            int inventoryNotifications = 0;
            actor.Inventory.Changed += () => inventoryNotifications++;

            ItemUseService service = new();
            ItemUseResult result = service.TryUseFromSlot(actor, actor.Inventory, 0);

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

            await context.DisposeNodeAsync(actor);
        });

        await context.RunAsync("ineligible-use-preserves-item", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            HealingItemUseEffectDefinition effect = new() { HealAmount = 35 };
            ItemDefinition medkit = TestDataFactory.CreateItem(
                "medkit",
                maxStackSize: 2,
                useEffect: effect);
            actor.Inventory.TryAdd(medkit, 1);
            int notifications = 0;
            actor.Inventory.Changed += () => notifications++;

            ItemUseResult result = new ItemUseService().TryUseFromSlot(
                actor,
                actor.Inventory,
                0);

            TestAssert.False(result.Success, "Full-health actor used a medkit.");
            TestAssert.False(result.ItemConsumed, "Failed item use reported consumption.");
            TestAssert.Equal(1, actor.Inventory.CountByItemId("medkit"),
                "Failed item use consumed the item.");
            TestAssert.Equal(0, notifications,
                "Failed item use published an inventory notification.");

            await context.DisposeNodeAsync(actor);
        });

        await context.RunAsync("ordinary-items-have-no-use-path", async () =>
        {
            TestHealthInventoryActorNode actor = context.AddNode(
                new TestHealthInventoryActorNode());
            await context.WaitProcessFramesAsync();
            ItemDefinition scrap = TestDataFactory.CreateItem("scrap");
            actor.Inventory.TryAdd(scrap, 1);

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
}
