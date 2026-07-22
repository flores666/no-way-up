using System;
using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Items;
using LineZero.Gameplay.Objectives;
using LineZero.Tests.Framework;
using LineZero.UI;
using LineZero.World3D;
using LineZero.World3D.Interaction;
using LineZero.World3D.Items;
using LineZero.World3D.Objectives;

namespace LineZero.Tests.Suites;

public sealed class World3DObjectiveFeatureTests : IFeatureTestSuite
{
    public string Id => "world-3d-objectives";

    public string Description =>
        "3D fuse transaction, powered presentation, completed door, dedicated exit sensor, and exactly-once terminal flow";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync(
            "movement-body-and-posture-cannot-activate-exit",
            async () =>
            {
                Node3D root = context.AddNode(new Node3D
                {
                    Name = "ObjectiveSensorIsolation3D"
                });
                PlayerController3D player = LoadPlayer();
                ObjectiveExitZone3D zone = LoadExitZone();
                player.Position = new Vector3(2.92f, 0.0f, 0.0f);
                root.AddChild(player);
                root.AddChild(zone);
                await context.WaitPhysicsFramesAsync(3);
                PlayerObjectiveSensor3D sensor =
                    player.GetNode<PlayerObjectiveSensor3D>(
                        "%PlayerObjectiveSensor3D");
                sensor.Bind(player);

                ObjectiveProgressModel objectives = CreateReachExitObjectives();
                zone.BindObjectives(objectives);
                await context.WaitPhysicsFramesAsync(2);
                TestAssert.Equal(
                    CollisionLayers3D.PlayerObjectiveSensor,
                    sensor.CollisionLayer,
                    "3D objective sensor does not use its dedicated layer.");
                TestAssert.Equal(
                    CollisionLayers3D.PlayerObjectiveSensor,
                    zone.CollisionMask,
                    "3D exit zone listens to a movement or presentation layer.");
                TestAssert.Equal(0, zone.GetOverlappingBodies().Count,
                    "3D exit zone detected the player movement body.");
                TestAssert.Equal(0, zone.GetOverlappingAreas().Count,
                    "Fixture accidentally placed the objective sensor inside.");

                player._UnhandledInput(Action("crawl"));
                await context.WaitPhysicsFramesAsync(2);
                TestAssert.True(player.IsUsingCrawlCollisionProfile,
                    "3D player did not enter Crawl for sensor-isolation coverage.");
                TestAssert.Equal(ObjectiveStage.ReachExit, objectives.CurrentStage,
                    "A stationary posture collider change completed the exit.");

                await context.DisposeNodeAsync(root);
            });

        await context.RunAsync(
            "fuse-power-and-door-publish-completed-state-safely",
            async () =>
            {
                Main3D main = await LoadMainWithoutLivingMutantAsync(context);
                TestAssert.Equal(ObjectiveStage.FindFuse, main.Objectives.CurrentStage,
                    "3D objective loop did not begin at FindFuse.");
                InteractionResult unpowered = main.EmergencyExitDoor.Interact(
                    new InteractionContext(main.Player));
                TestAssert.False(main.EmergencyExitDoor.IsOpening,
                    "Unpowered 3D emergency door started opening.");
                TestAssert.True(!string.IsNullOrWhiteSpace(unpowered.Message),
                    "Unpowered 3D emergency door returned no feedback.");

                int restoredEvents = 0;
                main.PowerCircuit.Changed += () =>
                    throw new InvalidOperationException(
                        "Expected 3D power Changed subscriber failure.");
                main.PowerCircuit.PowerRestored += () => restoredEvents++;
                WorldItemPickup3D fusePickup =
                    main.GetNode<Node3D>("%TestLevel3D")
                        .GetNode<WorldItemPickup3D>("%ReplacementFusePickup3D");
                InteractionResult pickup = fusePickup.Interact(
                    new InteractionContext(main.Player));
                TestAssert.True(!string.IsNullOrWhiteSpace(pickup.Message),
                    "3D replacement fuse pickup returned no completed result.");
                TestAssert.Equal(ObjectiveStage.RestorePower,
                    main.Objectives.CurrentStage,
                    "Acquiring the fuse did not advance the 3D objective.");
                TestAssert.Equal(1,
                    main.Player.Inventory.CountByItemId("replacement_fuse"),
                    "Replacement fuse was not conserved before installation.");

                InteractionResult installed = main.FuseBox.Interact(
                    new InteractionContext(main.Player));
                TestAssert.True(!string.IsNullOrWhiteSpace(installed.Message),
                    "Successful 3D fuse installation returned no result.");
                TestAssert.Equal(0,
                    main.Player.Inventory.CountByItemId("replacement_fuse"),
                    "Installed 3D fuse remained in inventory.");
                TestAssert.True(main.PowerCircuit.IsPowered,
                    "3D power circuit was not restored.");
                TestAssert.True(main.PoweredExitLight.IsPoweredPresentationActive,
                    "Powered 3D light did not consume completed circuit state.");
                TestAssert.Equal(ObjectiveStage.OpenExit,
                    main.Objectives.CurrentStage,
                    "Critical PowerRestored delivery was blocked by another subscriber.");
                TestAssert.Equal(1, restoredEvents,
                    "Power restoration did not publish exactly once.");

                main.FuseBox.Interact(new InteractionContext(main.Player));
                TestAssert.Equal(0,
                    main.Player.Inventory.CountByItemId("replacement_fuse"),
                    "Repeated installation duplicated or restored a fuse.");
                TestAssert.Equal(1, restoredEvents,
                    "Repeated installation republished power restoration.");

                int openedEvents = 0;
                main.EmergencyExitDoor.Opened += _ =>
                    throw new InvalidOperationException(
                        "Expected 3D door Opened subscriber failure.");
                main.EmergencyExitDoor.Opened += _ => openedEvents++;
                main.EmergencyExitDoor.Interact(new InteractionContext(main.Player));
                TestAssert.True(main.EmergencyExitDoor.IsOpening,
                    "Powered 3D emergency door did not start opening.");
                TestAssert.Equal(ObjectiveStage.OpenExit,
                    main.Objectives.CurrentStage,
                    "Objective advanced before the 3D door completed opening.");
                await context.WaitSecondsAsync(0.9);

                CollisionShape3D doorCollision =
                    main.EmergencyExitDoor.GetNode<CollisionShape3D>(
                        "%DoorCollision3D");
                TestAssert.True(main.EmergencyExitDoor.IsOpen,
                    "3D emergency door did not enter its terminal Open state.");
                TestAssert.True(doorCollision.Disabled,
                    "3D door completed before disabling movement collision.");
                TestAssert.Equal(ObjectiveStage.ReachExit,
                    main.Objectives.CurrentStage,
                    "3D objective did not advance after completed opening.");
                TestAssert.Equal(1, openedEvents,
                    "3D door Opened event did not safely publish exactly once.");

                main.EmergencyExitDoor.Interact(new InteractionContext(main.Player));
                await context.WaitSecondsAsync(0.1);
                TestAssert.Equal(1, openedEvents,
                    "Repeated input reopened the terminal 3D door.");
            });

        await context.RunAsync(
            "already-inside-exit-completes-full-loop-exactly-once",
            async () =>
            {
                Main3D main = await LoadMainWithoutLivingMutantAsync(context);
                main.Player.GlobalPosition = main.ExitZone.GlobalPosition;
                main.Player.Velocity = Vector3.Zero;
                await context.WaitPhysicsFramesAsync(4);
                TestAssert.Equal(ObjectiveStage.FindFuse, main.Objectives.CurrentStage,
                    "Early 3D exit entry completed the prototype.");

                int completedEvents = 0;
                main.ExitZone.EscapeCompleted += _ =>
                    throw new InvalidOperationException(
                        "Expected 3D completion subscriber failure.");
                main.ExitZone.EscapeCompleted += completedPlayer =>
                {
                    TestAssert.Same(main.Player, completedPlayer,
                        "3D completion supplied the wrong player.");
                    completedEvents++;
                };

                ItemDefinition replacementFuse =
                    ResourceLoader.Load<ItemDefinition>(
                        "res://data/items/ReplacementFuse.tres")
                    ?? throw new InvalidOperationException(
                        "Could not load the replacement fuse definition.");
                main.Player.Inventory.TryAdd(replacementFuse, 1);
                TestAssert.Equal(ObjectiveStage.RestorePower,
                    main.Objectives.CurrentStage,
                    "Inventory completion did not synchronize the fuse objective.");
                main.FuseBox.Interact(new InteractionContext(main.Player));
                main.EmergencyExitDoor.Interact(new InteractionContext(main.Player));
                await context.WaitSecondsAsync(0.9);

                TestAssert.Equal(ObjectiveStage.Completed,
                    main.Objectives.CurrentStage,
                    "Player already inside the 3D exit did not complete on ReachExit.");
                TestAssert.True(main.IsPrototypeCompleted && main.IsTerminalState,
                    "Completed 3D objective did not latch terminal state.");
                TestAssert.False(main.Player.CanAcceptGameplayInput,
                    "Completion left 3D player gameplay input active.");
                TestAssert.Equal(1, completedEvents,
                    "3D completion did not publish exactly once.");
                EscapeCompletePanelController panel =
                    main.GetNode<EscapeCompletePanelController>(
                        "%EscapeCompletePanel");
                TestAssert.True(panel.Visible,
                    "3D completion panel did not present completed model state.");

                main.Objectives.TryAdvanceTo(ObjectiveStage.Completed);
                await context.WaitProcessFramesAsync();
                TestAssert.Equal(1, completedEvents,
                    "Duplicate completion input republished the terminal event.");
            });

        await context.RunAsync("dead-player-cannot-complete-exit", async () =>
        {
            Main3D main = await LoadMainWithoutLivingMutantAsync(context);
            main.Player.GlobalPosition = main.ExitZone.GlobalPosition;
            await context.WaitPhysicsFramesAsync(3);
            main.Player.Health.ApplyDamage(new DamageInfo(
                main.Player.Health.MaxHealth,
                source: main.ExitZone,
                damageKind: "Objective death fixture"));
            TestAssert.True(main.Player.Health.IsDead && main.IsTerminalState,
                "Death fixture did not enter terminal state.");

            main.Objectives.TryAdvanceTo(ObjectiveStage.RestorePower);
            main.Objectives.TryAdvanceTo(ObjectiveStage.OpenExit);
            main.Objectives.TryAdvanceTo(ObjectiveStage.ReachExit);
            await context.WaitPhysicsFramesAsync(2);
            TestAssert.Equal(ObjectiveStage.ReachExit,
                main.Objectives.CurrentStage,
                "Dead 3D player incorrectly completed the exit.");
            TestAssert.False(main.IsPrototypeCompleted,
                "Death was replaced by prototype completion.");
        });
    }

    private static async Task<Main3D> LoadMainWithoutLivingMutantAsync(
        FeatureTestContext context)
    {
        Main3D main = context.InstantiateScene<Main3D>(
            "res://scenes/3d/Main3D.tscn");
        await context.WaitPhysicsFramesAsync(5);
        main.Mutant.Health.ApplyDamage(new DamageInfo(
            main.Mutant.Health.MaxHealth,
            source: main.Player,
            damageKind: "Objective test isolation"));
        return main;
    }

    private static PlayerController3D LoadPlayer()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://scenes/3d/player/Player3D.tscn")
            ?? throw new InvalidOperationException("Could not load Player3D.");
        return scene.Instantiate<PlayerController3D>();
    }

    private static ObjectiveExitZone3D LoadExitZone()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://scenes/3d/levels/ObjectiveExitZone3D.tscn")
            ?? throw new InvalidOperationException("Could not load ObjectiveExitZone3D.");
        return scene.Instantiate<ObjectiveExitZone3D>();
    }

    private static ObjectiveProgressModel CreateReachExitObjectives()
    {
        ObjectiveProgressModel objectives = new();
        objectives.TryAdvanceTo(ObjectiveStage.RestorePower);
        objectives.TryAdvanceTo(ObjectiveStage.OpenExit);
        objectives.TryAdvanceTo(ObjectiveStage.ReachExit);
        return objectives;
    }

    private static InputEventAction Action(string action)
    {
        return new InputEventAction
        {
            Action = action,
            Pressed = true,
            Strength = 1.0f
        };
    }
}
