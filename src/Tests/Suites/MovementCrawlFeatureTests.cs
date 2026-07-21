using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Movement;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Hazards;
using LineZero.World2D.Levels;
using LineZero.World2D.Noise;
using LineZero.World2D.Perception;

namespace LineZero.Tests.Suites;

public sealed class MovementCrawlFeatureTests : IFeatureTestSuite
{
    public string Id => "movement-crawl";

    public string Description => "Walk/Crouch/Crawl profiles, blocked exit, and constant sensors";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("crawl-switch-keeps-exactly-one-movement-collider", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "MovementTestRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            await context.WaitProcessFramesAsync(2);

            CollisionShape2D normal = player.GetNode<CollisionShape2D>(
                "%NormalCollisionShape");
            CollisionShape2D crawl = player.GetNode<CollisionShape2D>(
                "%CrawlCollisionShape");
            TestAssert.False(normal.Disabled, "Normal collider did not start active.");
            TestAssert.True(crawl.Disabled, "Crawl collider did not start disabled.");

            player._UnhandledInput(Action("crawl"));
            await WaitForCollisionProfileUpdateAsync(context);
            TestAssert.True(player.IsUsingCrawlCollisionProfile,
                "Crawl input did not activate Crawl profile.");
            TestAssert.True(normal.Disabled ^ crawl.Disabled,
                "Crawl transition did not leave exactly one active collider.");
            TestAssert.Equal(MovementMode.Crawl, player.CurrentMovementMode,
                "Crawl transition did not update movement mode.");

            player._UnhandledInput(Action("crawl"));
            await WaitForCollisionProfileUpdateAsync(context);
            TestAssert.False(player.IsUsingCrawlCollisionProfile,
                "Clear crawl exit did not restore normal profile.");
            TestAssert.True(normal.Disabled ^ crawl.Disabled,
                "Crawl exit did not leave exactly one active collider.");
            TestAssert.Equal(MovementMode.Crouch, player.CurrentMovementMode,
                "Crawl exit did not restore requested crouch posture.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("blocked-crawl-exit-preserves-crawl-and-rejects-once", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "BlockedCrawlRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            await context.WaitProcessFramesAsync(2);
            player._UnhandledInput(Action("crawl"));
            await WaitForCollisionProfileUpdateAsync(context);

            StaticBody2D ceiling = new()
            {
                Name = "LowCeiling",
                CollisionLayer = CollisionLayers2D.World,
                CollisionMask = CollisionLayers2D.World,
            };
            ceiling.AddChild(new CollisionShape2D
            {
                Position = new Vector2(0.0f, -12.0f),
                Shape = new RectangleShape2D { Size = new Vector2(40.0f, 4.0f) },
            });
            root.AddChild(ceiling);
            await context.WaitPhysicsFramesAsync(2);
            int rejections = 0;
            player.PostureChangeRejected += message =>
            {
                if (message == "Cannot stand here.")
                {
                    rejections++;
                }
            };

            player._UnhandledInput(Action("crawl"));
            await WaitForCollisionProfileUpdateAsync(context);

            CollisionShape2D normal = player.GetNode<CollisionShape2D>(
                "%NormalCollisionShape");
            CollisionShape2D crawl = player.GetNode<CollisionShape2D>(
                "%CrawlCollisionShape");
            TestAssert.True(player.IsUsingCrawlCollisionProfile,
                "Blocked exit enabled the normal profile.");
            TestAssert.True(normal.Disabled && !crawl.Disabled,
                "Blocked exit left an invalid collider state.");
            TestAssert.Equal(MovementMode.Crawl, player.CurrentMovementMode,
                "Blocked exit changed posture.");
            TestAssert.Equal(1, rejections,
                "Blocked exit did not emit exactly one rejection.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("posture-does-not-change-dedicated-sensors", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "SensorStabilityRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            await context.WaitProcessFramesAsync(2);

            LightExposureSensor2D lightSensor = player.GetNode<LightExposureSensor2D>(
                "%LightExposureSensor2D");
            PlayerHazardSensor2D hazardSensor = player.GetNode<PlayerHazardSensor2D>(
                "%PlayerHazardSensor2D");
            PlayerObjectiveSensor2D objectiveSensor = player.GetNode<PlayerObjectiveSensor2D>(
                "%PlayerObjectiveSensor2D");
            CollisionShape2D lightShape = lightSensor.GetNode<CollisionShape2D>(
                "%LightExposureShape");
            CollisionShape2D hazardShape = hazardSensor.GetNode<CollisionShape2D>(
                "%HazardShape");
            CollisionShape2D objectiveShape = objectiveSensor.GetNode<CollisionShape2D>(
                "%ObjectiveShape");
            Shape2D lightResource = lightShape.Shape!;
            Shape2D hazardResource = hazardShape.Shape!;
            Shape2D objectiveResource = objectiveShape.Shape!;
            Transform2D lightTransform = lightSensor.Transform;
            Transform2D hazardTransform = hazardSensor.Transform;
            Transform2D objectiveTransform = objectiveSensor.Transform;

            player._UnhandledInput(Action("crouch"));
            player._UnhandledInput(Action("crawl"));
            await context.WaitProcessFramesAsync(2);

            TestAssert.Same(lightResource, lightShape.Shape!,
                "Posture replaced the light-exposure sensor shape.");
            TestAssert.Same(hazardResource, hazardShape.Shape!,
                "Posture replaced the hazard sensor shape.");
            TestAssert.Same(objectiveResource, objectiveShape.Shape!,
                "Posture replaced the objective sensor shape.");
            TestAssert.Equal(lightTransform, lightSensor.Transform,
                "Posture moved or resized the light sensor.");
            TestAssert.Equal(hazardTransform, hazardSensor.Transform,
                "Posture moved or resized the hazard sensor.");
            TestAssert.Equal(objectiveTransform, objectiveSensor.Transform,
                "Posture moved or resized the objective sensor.");

            await context.DisposeNodeAsync(root);
        });
    }

    private static async Task WaitForCollisionProfileUpdateAsync(
        FeatureTestContext context)
    {
        await context.WaitProcessFramesAsync();
        await context.WaitPhysicsFramesAsync();
        await context.WaitProcessFramesAsync();
    }

    private static InputEventAction Action(string action)
    {
        return new InputEventAction
        {
            Action = action,
            Pressed = true,
            Strength = 1.0f,
        };
    }

    private static PlayerController2D LoadPlayer()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>("res://scenes/player/Player.tscn")
            ?? throw new System.InvalidOperationException("Could not load player scene.");
        return scene.Instantiate<PlayerController2D>();
    }
}
