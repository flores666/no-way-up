using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Objectives;
using LineZero.Gameplay.Power;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Interaction;
using LineZero.World2D.Levels;
using LineZero.World2D.Noise;

namespace LineZero.Tests.Suites;

public sealed class EmergencyExitFeatureTests : IFeatureTestSuite
{
    public string Id => "emergency-exit";

    public string Description => "Powered door completion, one-shot opening, early zone overlap, and terminal progression";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("objective-advances-only-after-door-fully-opens", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "DoorTestRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            Node actor = new() { Name = "Actor" };
            SlidingDoor2D door = LoadDoor(requiresPower: true, animationDuration: 0.05);
            root.AddChild(noiseSystem);
            root.AddChild(actor);
            root.AddChild(door);
            await context.WaitProcessFramesAsync(2);
            door.BindNoiseSystem(noiseSystem);
            PowerCircuitModel circuit = new();
            door.BindPowerCircuit(circuit);
            ObjectiveProgressModel objectives = CreateOpenExitObjectives();
            int openedEvents = 0;
            door.Opened += openedDoor =>
            {
                TestAssert.Same(door, openedDoor,
                    "Door Opened event supplied the wrong door.");
                openedEvents++;
                objectives.TryAdvanceTo(ObjectiveStage.ReachExit);
            };

            InteractionResult unpowered = door.Interact(new InteractionContext(actor));
            TestAssert.False(door.IsOpening || door.IsOpen,
                "Unpowered emergency door started opening.");
            TestAssert.True(!string.IsNullOrWhiteSpace(unpowered.Message),
                "Unpowered door returned no feedback.");

            circuit.TryInstallFuse();
            door.Interact(new InteractionContext(actor));
            TestAssert.True(door.IsOpening, "Powered door did not enter Opening state.");
            TestAssert.Equal(ObjectiveStage.OpenExit, objectives.CurrentStage,
                "Objective advanced before opening completion.");
            TestAssert.Equal(0, openedEvents,
                "Opened event fired before the tween completed.");

            await context.WaitSecondsAsync(0.15);
            CollisionShape2D collision = door.GetNode<CollisionShape2D>("%DoorCollision");
            CollisionShape2D interactionShape = door.GetNode<CollisionShape2D>(
                "%InteractionShape");
            TestAssert.True(door.IsOpen, "Door did not enter terminal Open state.");
            TestAssert.True(collision.Disabled,
                "Opened event occurred without disabling blocking collision.");
            TestAssert.True(interactionShape.Disabled,
                "Open door remained interactable.");
            TestAssert.Equal(1, openedEvents, "Opened event was not exactly-once.");
            TestAssert.Equal(ObjectiveStage.ReachExit, objectives.CurrentStage,
                "Objective did not advance after full opening.");

            door.Interact(new InteractionContext(actor));
            await context.WaitSecondsAsync(0.1);
            TestAssert.Equal(1, openedEvents,
                "Repeated interaction reopened an already open door.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("interrupted-opening-does-not-publish-completion", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "CanceledDoorRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            Node actor = new() { Name = "Actor" };
            SlidingDoor2D door = LoadDoor(requiresPower: false, animationDuration: 0.5);
            root.AddChild(noiseSystem);
            root.AddChild(actor);
            root.AddChild(door);
            await context.WaitProcessFramesAsync(2);
            door.BindNoiseSystem(noiseSystem);
            int opened = 0;
            door.Opened += _ => opened++;

            door.Interact(new InteractionContext(actor));
            door.QueueFree();
            await context.WaitSecondsAsync(0.6);

            TestAssert.Equal(0, opened,
                "Deleted or canceled opening published Opened.");
            await context.DisposeNodeAsync(root);
        });


        await context.RunAsync("movement-body-and-posture-switch-cannot-complete-exit", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "SensorOnlyExitRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            ObjectiveExitZone2D zone = LoadExitZone();
            zone.GlobalPosition = Vector2.Zero;
            player.GlobalPosition = new Vector2(122.0f, 0.0f);
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            root.AddChild(zone);
            await context.WaitPhysicsFramesAsync(3);

            TestAssert.Equal(CollisionLayers2D.PlayerObjectiveSensor, zone.CollisionMask,
                "Exit zone listens to a non-objective collision layer.");
            TestAssert.True(zone.GetOverlappingBodies().Count == 0,
                "Sensor-only exit zone reported movement-body overlaps.");
            TestAssert.True(zone.GetOverlappingAreas().Count == 0,
                "Test fixture accidentally placed the objective sensor inside.");

            ObjectiveProgressModel objectives = CreateOpenExitObjectives();
            objectives.TryAdvanceTo(ObjectiveStage.ReachExit);
            zone.BindObjectives(objectives);
            await context.WaitProcessFramesAsync();
            TestAssert.Equal(ObjectiveStage.ReachExit, objectives.CurrentStage,
                "Movement body completed a sensor-only exit.");

            player._UnhandledInput(Action("crawl"));
            await WaitForPostureTransitionAsync(context);
            player._UnhandledInput(Action("crawl"));
            await WaitForPostureTransitionAsync(context);
            TestAssert.Equal(ObjectiveStage.ReachExit, objectives.CurrentStage,
                "Stationary movement-collider switching activated the exit.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("early-exit-overlap-completes-on-reach-exit", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "ExitZoneTestRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            ObjectiveExitZone2D zone = LoadExitZone();
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            root.AddChild(zone);
            await context.WaitPhysicsFramesAsync(3);
            ObjectiveProgressModel objectives = CreateOpenExitObjectives();
            int completedEvents = 0;
            zone.EscapeCompleted += completedPlayer =>
            {
                TestAssert.Same(player, completedPlayer,
                    "Exit completion supplied the wrong player.");
                completedEvents++;
            };
            zone.BindObjectives(objectives);

            TestAssert.Equal(ObjectiveStage.OpenExit, objectives.CurrentStage,
                "Early overlap completed the objective too soon.");
            objectives.TryAdvanceTo(ObjectiveStage.ReachExit);
            await context.WaitProcessFramesAsync();

            TestAssert.Equal(ObjectiveStage.Completed, objectives.CurrentStage,
                "Player already inside did not complete on ReachExit.");
            TestAssert.Equal(1, completedEvents,
                "Exit completion was not exactly-once.");
            objectives.TryAdvanceTo(ObjectiveStage.Completed);
            TestAssert.Equal(1, completedEvents,
                "Duplicate objective change republished completion.");

            await context.DisposeNodeAsync(root);
        });
    }

    private static async Task WaitForPostureTransitionAsync(
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

    private static SlidingDoor2D LoadDoor(bool requiresPower, double animationDuration)
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://scenes/interactables/SlidingDoor2D.tscn")
            ?? throw new System.InvalidOperationException("Could not load sliding door scene.");
        SlidingDoor2D door = scene.Instantiate<SlidingDoor2D>();
        door.RequiresPower = requiresPower;
        door.AnimationDuration = animationDuration;
        return door;
    }

    private static ObjectiveExitZone2D LoadExitZone()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://scenes/levels/ObjectiveExitZone2D.tscn")
            ?? throw new System.InvalidOperationException("Could not load exit zone scene.");
        return scene.Instantiate<ObjectiveExitZone2D>();
    }

    private static PlayerController2D LoadPlayer()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>("res://scenes/player/Player.tscn")
            ?? throw new System.InvalidOperationException("Could not load player scene.");
        return scene.Instantiate<PlayerController2D>();
    }

    private static ObjectiveProgressModel CreateOpenExitObjectives()
    {
        ObjectiveProgressModel objectives = new();
        objectives.TryAdvanceTo(ObjectiveStage.RestorePower);
        objectives.TryAdvanceTo(ObjectiveStage.OpenExit);
        return objectives;
    }
}
