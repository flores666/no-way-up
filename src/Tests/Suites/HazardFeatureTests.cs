using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Health;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Hazards;

namespace LineZero.Tests.Suites;

public sealed class HazardFeatureTests : IFeatureTestSuite
{
    public string Id => "hazards";

    public string Description => "Stable sensor overlap, immediate damage, periodic catch-up, and terminal safety";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("entry-and-periodic-timing-preserve-remainder", async () =>
        {
            Node2D targetRoot = CreateHazardTarget(context);
            HealthComponent healthComponent = targetRoot.GetNode<HealthComponent>("HealthComponent");
            DamageZone2D zone = context.InstantiateScene<DamageZone2D>(
                "res://scenes/hazards/DamageZone2D.tscn");
            zone.Position = Vector2.Zero;
            await context.WaitPhysicsFramesAsync(2);
            zone.SetPhysicsProcess(false);

            TestAssert.Equal(90, healthComponent.Health.CurrentHealth,
                "Hazard did not apply exactly one immediate-entry hit.");

            zone._PhysicsProcess(3.25);
            TestAssert.Equal(60, healthComponent.Health.CurrentHealth,
                "Three seconds of overlap did not produce three periodic ticks.");
            zone._PhysicsProcess(0.74);
            TestAssert.Equal(60, healthComponent.Health.CurrentHealth,
                "Periodic remainder produced an early tick.");
            zone._PhysicsProcess(0.01);
            TestAssert.Equal(50, healthComponent.Health.CurrentHealth,
                "Periodic remainder was not preserved after a late frame.");

            await context.DisposeNodeAsync(zone);
            await context.DisposeNodeAsync(targetRoot);
        });

        await context.RunAsync("catch-up-is-bounded-and-debt-is-retained", async () =>
        {
            Node2D targetRoot = CreateHazardTarget(context);
            HealthComponent healthComponent = targetRoot.GetNode<HealthComponent>("HealthComponent");
            DamageZone2D zone = context.InstantiateScene<DamageZone2D>(
                "res://scenes/hazards/DamageZone2D.tscn");
            zone.DamageAmount = 1;
            await context.WaitPhysicsFramesAsync(2);
            zone.SetPhysicsProcess(false);
            int afterEntry = healthComponent.Health.CurrentHealth;

            zone._PhysicsProcess(10.25);
            TestAssert.Equal(afterEntry - 4, healthComponent.Health.CurrentHealth,
                "Hazard exceeded or missed the four-tick catch-up cap.");
            zone._PhysicsProcess(0.01);
            TestAssert.Equal(afterEntry - 8, healthComponent.Health.CurrentHealth,
                "Due periodic debt was discarded instead of carried forward.");

            await context.DisposeNodeAsync(zone);
            await context.DisposeNodeAsync(targetRoot);
        });

        await context.RunAsync("exit-and-completion-stop-future-damage", async () =>
        {
            Node2D targetRoot = CreateHazardTarget(context);
            HealthComponent healthComponent = targetRoot.GetNode<HealthComponent>("HealthComponent");
            DamageZone2D zone = context.InstantiateScene<DamageZone2D>(
                "res://scenes/hazards/DamageZone2D.tscn");
            await context.WaitPhysicsFramesAsync(2);
            zone.SetPhysicsProcess(false);
            int healthAfterEntry = healthComponent.Health.CurrentHealth;

            targetRoot.Position = new Vector2(500.0f, 0.0f);
            await context.WaitPhysicsFramesAsync(2);
            zone._PhysicsProcess(10.0);
            TestAssert.Equal(healthAfterEntry, healthComponent.Health.CurrentHealth,
                "Exited target retained hazard timer debt.");

            targetRoot.Position = Vector2.Zero;
            await context.WaitPhysicsFramesAsync(2);
            healthComponent.Health.DisableDamagePermanently();
            int completedHealth = healthComponent.Health.CurrentHealth;
            zone._PhysicsProcess(10.0);
            TestAssert.Equal(completedHealth, healthComponent.Health.CurrentHealth,
                "Completed target received hazard damage.");

            await context.DisposeNodeAsync(zone);
            await context.DisposeNodeAsync(targetRoot);
        });
    }

    private static Node2D CreateHazardTarget(FeatureTestContext context)
    {
        Node2D root = new() { Name = "HazardTarget", Position = Vector2.Zero };
        HealthComponent health = new() { Name = "HealthComponent", MaxHealth = 100 };
        PlayerHazardSensor2D sensor = new()
        {
            Name = "PlayerHazardSensor2D",
            CollisionLayer = CollisionLayers2D.PlayerHazardSensor,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = true,
            HealthTargetPath = new NodePath("../HealthComponent"),
            SensorShapePath = new NodePath("HazardShape"),
        };
        sensor.AddChild(new CollisionShape2D
        {
            Name = "HazardShape",
            Shape = new CircleShape2D { Radius = 10.0f },
        });
        root.AddChild(health);
        root.AddChild(sensor);
        return context.AddNode(root);
    }
}
