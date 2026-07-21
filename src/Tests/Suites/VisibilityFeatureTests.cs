using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Perception;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Perception;

namespace LineZero.Tests.Suites;

public sealed class VisibilityFeatureTests : IFeatureTestSuite
{
    public string Id => "visibility";

    public string Description => "Posture, ambient zones, flashlight, priorities, and death state";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("multipliers-compose-and-update-through-events", async () =>
        {
            PlayerVisibilityController2D controller = context.AddNode(
                new PlayerVisibilityController2D
                {
                    Name = "VisibilityController",
                    FlashlightOnMultiplier = 1.45f,
                });
            LightExposureZone2D dark = CreateZone(
                context,
                "Dark",
                multiplier: 0.70f,
                priority: 10);
            await context.WaitProcessFramesAsync();

            TestMovementModeSource movement = new();
            FlashlightModel flashlight = new(
                TestDataFactory.CreateFlashlightDefinition(),
                startOn: false);
            HealthModel health = new(100);
            controller.Initialize(
                movement,
                TestDataFactory.CreateMovementSettings(),
                flashlight,
                health);

            TestAssert.NearlyEqual(1.0, controller.State.FinalMultiplier, 1e-6,
                "Default visibility was incorrect.");
            controller.EnterZone(dark);
            TestAssert.NearlyEqual(0.70, controller.State.FinalMultiplier, 1e-6,
                "Dark-zone multiplier was not applied.");
            TestAssert.Equal(VisibilityCategory.Dim, controller.State.Category,
                "Dark Walk category was incorrect.");

            movement.SetMode(MovementMode.Crouch);
            TestAssert.NearlyEqual(0.455, controller.State.FinalMultiplier, 1e-6,
                "Crouch and darkness did not compose.");
            TestAssert.Equal(VisibilityCategory.Hidden, controller.State.Category,
                "Crouch darkness was not hidden.");

            flashlight.TryTurnOn();
            TestAssert.NearlyEqual(0.65975, controller.State.FinalMultiplier, 1e-6,
                "Flashlight multiplier did not update immediately.");
            TestAssert.Equal(VisibilityCategory.Dim, controller.State.Category,
                "Flashlight visibility category was incorrect.");

            await context.DisposeNodeAsync(dark);
            await context.DisposeNodeAsync(controller);
        });

        await context.RunAsync("overlap-priority-and-tie-break-are-deterministic", async () =>
        {
            PlayerVisibilityController2D controller = context.AddNode(
                new PlayerVisibilityController2D { Name = "VisibilityController" });
            LightExposureZone2D lowPriority = CreateZone(
                context,
                "Low priority",
                multiplier: 0.70f,
                priority: 5);
            LightExposureZone2D zeta = CreateZone(
                context,
                "Zeta",
                multiplier: 1.20f,
                priority: 10);
            LightExposureZone2D alpha = CreateZone(
                context,
                "Alpha",
                multiplier: 1.25f,
                priority: 10);
            await context.WaitProcessFramesAsync();

            controller.Initialize(
                new TestMovementModeSource(),
                TestDataFactory.CreateMovementSettings(),
                new FlashlightModel(TestDataFactory.CreateFlashlightDefinition(), false),
                new HealthModel(100));
            controller.EnterZone(lowPriority);
            controller.EnterZone(zeta);
            controller.EnterZone(alpha);

            TestAssert.Equal("Alpha", controller.State.AmbientZoneName,
                "Equal-priority zone selection was not deterministic by name.");
            TestAssert.NearlyEqual(1.25, controller.State.AmbientLightMultiplier, 1e-6,
                "Selected zone multiplier was incorrect.");

            controller.ExitZone(alpha);
            TestAssert.Equal("Zeta", controller.State.AmbientZoneName,
                "Zone fallback after exit was incorrect.");

            await context.DisposeNodeAsync(alpha);
            await context.DisposeNodeAsync(zeta);
            await context.DisposeNodeAsync(lowPriority);
            await context.DisposeNodeAsync(controller);
        });

        await context.RunAsync("death-updates-visibility-life-state", async () =>
        {
            PlayerVisibilityController2D controller = context.AddNode(
                new PlayerVisibilityController2D { Name = "VisibilityController" });
            await context.WaitProcessFramesAsync();
            HealthModel health = new(100);
            int notifications = 0;
            controller.VisibilityChanged += _ => notifications++;
            controller.Initialize(
                new TestMovementModeSource(),
                TestDataFactory.CreateMovementSettings(),
                new FlashlightModel(TestDataFactory.CreateFlashlightDefinition(), false),
                health);

            health.ApplyDamage(new LineZero.Gameplay.Health.DamageInfo(100));

            TestAssert.False(controller.State.IsActorAlive,
                "Visibility state still reported a dead actor as alive.");
            TestAssert.Equal(1, notifications,
                "Death did not produce exactly one visibility notification.");
            await context.DisposeNodeAsync(controller);
        });
    }

    private static LightExposureZone2D CreateZone(
        FeatureTestContext context,
        string displayName,
        float multiplier,
        int priority)
    {
        LightExposureZone2D zone = new()
        {
            Name = displayName.Replace(' ', '_'),
            DisplayName = displayName,
            VisibilityMultiplier = multiplier,
            ZonePriority = priority,
            CollisionLayer = 0,
            CollisionMask = CollisionLayers2D.LightExposureSensor,
        };
        zone.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(100.0f, 100.0f) },
        });
        return context.AddNode(zone);
    }
}
