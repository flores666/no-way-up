using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Items;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Timing;
using LineZero.Tests.Framework;
using LineZero.World3D;
using LineZero.World3D.Hazards;
using LineZero.World3D.Interaction;
using LineZero.World3D.Items;

namespace LineZero.Tests.Suites;

public sealed class World3DGameplayFeatureTests : IFeatureTestSuite
{
    public string Id => "world-3d-gameplay";

    public string Description =>
        "3D interaction, inventory, health, hazard, modal, and terminal contracts";

    public async Task RunAsync(FeatureTestContext context)
    {
        context.Run("interaction-candidate-scoring-is-stable", () =>
        {
            float nearAligned = InteractionCandidateScorer.Calculate(
                distance: 1.0f,
                interactionRange: 4.0f,
                alignment: 1.0f,
                priority: 0);
            float farBehind = InteractionCandidateScorer.Calculate(
                distance: 3.8f,
                interactionRange: 4.0f,
                alignment: -1.0f,
                priority: 0);

            TestAssert.True(nearAligned > farBehind,
                "3D candidate scoring did not prefer a near aligned target.");
            TestAssert.False(
                InteractionCandidateScorer.IsClearlyBetter(
                    nearAligned,
                    nearAligned + 0.02f,
                    switchThreshold: 0.08f),
                "Candidate hysteresis accepted an insignificant score change.");
        });

        context.Run("bounded-periodic-timer-preserves-excess-debt", () =>
        {
            PeriodicCatchUpTimer timer = new(
                intervalSeconds: 1.0,
                maximumTicksPerAdvance: 4);
            PeriodicCatchUpResult first = timer.Advance(6.25);
            TestAssert.Equal(4, first.DueTicks,
                "Long-frame catch-up exceeded or missed its bound.");
            TestAssert.NearlyEqual(2.25, first.RemainingDebtSeconds, 0.000001,
                "Bounded catch-up discarded elapsed-time debt.");

            PeriodicCatchUpResult second = timer.Advance(0.75);
            TestAssert.Equal(3, second.DueTicks,
                "Preserved timing debt was not applied on the next advance.");
            TestAssert.NearlyEqual(0.0, second.RemainingDebtSeconds, 0.000001,
                "Periodic timer did not subtract complete intervals.");
        });

        await context.RunAsync("player-owns-models-and-constant-hazard-sensor", async () =>
        {
            PlayerController3D player = context.InstantiateScene<PlayerController3D>(
                "res://scenes/3d/player/Player3D.tscn");
            await context.WaitPhysicsFramesAsync(2);

            TestAssert.Equal(12, player.Inventory.Capacity,
                "Player3D did not expose its composed inventory model.");
            TestAssert.Equal(100, player.Health.MaxHealth,
                "Player3D did not expose its composed health model.");
            PlayerHazardSensor3D sensor =
                player.GetNode<PlayerHazardSensor3D>("%PlayerHazardSensor3D");
            CollisionShape3D sensorShape =
                sensor.GetNode<CollisionShape3D>("%PlayerHazardSensorShape3D");
            Shape3D shapeBefore = sensorShape.Shape;
            Transform3D transformBefore = sensorShape.Transform;

            TestAssert.True(player.TrySetPosture(MovementMode.Crawl),
                "Player3D could not enter Crawl for sensor-independence validation.");
            TestAssert.Same(shapeBefore, sensorShape.Shape,
                "Crawl replaced the dedicated hazard sensor shape.");
            TestAssert.Equal(transformBefore, sensorShape.Transform,
                "Crawl changed the dedicated hazard sensor transform.");
        });

        await context.RunAsync("pickup-mutates-inventory-once", async () =>
        {
            PlayerController3D player = context.InstantiateScene<PlayerController3D>(
                "res://scenes/3d/player/Player3D.tscn");
            WorldItemPickup3D pickup = context.InstantiateScene<WorldItemPickup3D>(
                "res://scenes/3d/items/WorldItemPickup3D.tscn");
            await context.WaitProcessFramesAsync();

            ItemDefinition item = pickup.ItemDefinition
                ?? throw new TestAssertionException("3D pickup has no item definition.");
            InteractionContext interaction = new(player);
            int before = player.Inventory.CountByItemId(item.Id);
            InteractionResult first = pickup.Interact(interaction);
            InteractionResult second = pickup.Interact(interaction);

            TestAssert.True(!string.IsNullOrWhiteSpace(first.Message),
                "Successful 3D pickup did not return completed interaction feedback.");
            TestAssert.True(string.IsNullOrWhiteSpace(second.Message),
                "A second logical interaction duplicated a queued 3D pickup.");
            TestAssert.Equal(before + 1, player.Inventory.CountByItemId(item.Id),
                "One 3D pickup interaction did not conserve its item quantity.");
        });

        await context.RunAsync(
            "interaction-excludes-target-body-blocks-walls-and-ignores-key-echo",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(6);
                LootContainer3D container = main.GetNode<Node3D>("%TestLevel3D")
                    .GetNode<LootContainer3D>("%EmergencyCabinet3D");
                PlayerInteractor3D interactor =
                    main.Player.GetNode<PlayerInteractor3D>(
                        "%PlayerInteractionSensor3D");
                PlayerAimController3D aim =
                    main.Player.GetNode<PlayerAimController3D>(
                        "%PlayerAimController3D");

                main.Player.SetPhysicsProcess(false);
                main.Player.GlobalPosition = container.GlobalPosition +
                                             (Vector3.Back * 2.0f);
                main.Player.Velocity = Vector3.Zero;
                aim.SetProcess(false);
                TestAssert.True(
                    aim.TryApplyWorldAimPoint(container.GlobalPosition),
                    "3D interaction fixture could not establish a stable aim point.");
                await context.WaitPhysicsFramesAsync(6);
                Interactable3D unobstructedTarget = interactor.CurrentTarget
                    ?? throw new TestAssertionException(
                        "The unobstructed 3D interaction target was missing.");
                TestAssert.Same(container, unobstructedTarget,
                    "An interactable's own physical body blocked its interaction ray.");

                StaticBody3D wall = context.AddNode(new StaticBody3D
                {
                    Name = "InteractionBlockingWall3D",
                    CollisionLayer = CollisionLayers3D.World,
                    CollisionMask = 0,
                    Position = container.GlobalPosition + Vector3.Back
                });
                wall.AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D
                    {
                        Size = new Vector3(1.8f, 2.0f, 0.2f)
                    },
                    Position = new Vector3(0.0f, 1.0f, 0.0f)
                });
                await context.WaitPhysicsFramesAsync(6);
                CollisionObject3D targetOccluder = container.InteractionOccluder
                    ?? throw new TestAssertionException(
                        "The 3D container has no physical interaction occluder.");
                Godot.Collections.Array<Rid> exclusions = new()
                {
                    main.Player.GetRid(),
                    targetOccluder.GetRid()
                };
                PhysicsRayQueryParameters3D blockingQuery =
                    PhysicsRayQueryParameters3D.Create(
                        main.Player.GlobalPosition + (Vector3.Up * 0.75f),
                        container.InteractionPosition + (Vector3.Up * 0.5f),
                        CollisionLayers3D.World,
                        exclusions);
                blockingQuery.CollideWithAreas = false;
                blockingQuery.CollideWithBodies = true;
                blockingQuery.HitFromInside = true;
                Godot.Collections.Dictionary blockingHit =
                    main.Player.GetWorld3D().DirectSpaceState.IntersectRay(
                        blockingQuery);
                TestAssert.True(blockingHit.Count > 0,
                    "The fixture wall was not registered on the interaction ray.");
                interactor._PhysicsProcess(interactor.SelectionIntervalSeconds);
                TestAssert.False(
                    ReferenceEquals(interactor.CurrentTarget, container),
                    "World geometry did not block the selected 3D interaction target.");

                wall.GlobalPosition += Vector3.Right * 5.0f;
                await context.WaitPhysicsFramesAsync(6);
                interactor._PhysicsProcess(interactor.SelectionIntervalSeconds);
                Interactable3D restoredTarget = interactor.CurrentTarget
                    ?? throw new TestAssertionException(
                        "The unblocked 3D interaction target was not restored.");
                TestAssert.Same(container, restoredTarget,
                    "Removing the world blocker did not restore the interaction target.");

                int completedInteractions = 0;
                interactor.InteractionCompleted += (_, _, _) =>
                    completedInteractions++;
                interactor._UnhandledInput(new InputEventKey
                {
                    PhysicalKeycode = Key.E,
                    Pressed = true,
                    Echo = false
                });
                interactor._UnhandledInput(new InputEventKey
                {
                    PhysicalKeycode = Key.E,
                    Pressed = true,
                    Echo = true
                });
                TestAssert.Equal(1, completedInteractions,
                    "One physical interaction key press produced multiple actions.");
            });

        await context.RunAsync("medkit-transaction-works-through-player3d-models", async () =>
        {
            PlayerController3D player = context.InstantiateScene<PlayerController3D>(
                "res://scenes/3d/player/Player3D.tscn");
            await context.WaitProcessFramesAsync();
            ItemDefinition medkit = ResourceLoader.Load<ItemDefinition>(
                "res://data/items/Medkit.tres")
                ?? throw new TestAssertionException("Could not load Medkit.tres.");
            player.Inventory.TryAdd(medkit, 1);
            player.Health.ApplyDamage(new DamageInfo(40, player, "Test"));
            int healthBefore = player.Health.CurrentHealth;

            ItemUseResult result = new ItemUseService().TryUseFromSlot(
                player,
                player.Inventory,
                slotIndex: 0);

            TestAssert.True(result.Success && result.ItemConsumed,
                "3D player medkit transaction did not complete.");
            TestAssert.True(player.Health.CurrentHealth > healthBefore,
                "3D player medkit transaction did not heal.");
            TestAssert.Equal(0, player.Inventory.CountByItemId(medkit.Id),
                "3D player medkit transaction did not consume exactly one item.");
        });

        await context.RunAsync("hazard-overlap-damages-and-death-is-terminal", async () =>
        {
            Main3D main = context.InstantiateScene<Main3D>(
                "res://scenes/3d/Main3D.tscn");
            await context.WaitPhysicsFramesAsync(8);
            DamageZone3D hazard = main.GetNode<DamageZone3D>(
                "TestLevel3D/Gameplay/DamageZone3D");
            int healthBefore = main.Player.Health.CurrentHealth;

            main.Player.GlobalPosition = hazard.GlobalPosition;
            main.Player.Velocity = Vector3.Zero;
            await context.WaitPhysicsFramesAsync(4);
            TestAssert.True(main.Player.Health.CurrentHealth < healthBefore,
                "Dedicated 3D hazard overlap did not apply damage.");

            main.Player.Health.ApplyDamage(
                new DamageInfo(main.Player.Health.CurrentHealth, hazard, "Test lethal"));
            TestAssert.True(main.Player.IsTerminalState,
                "3D player death did not enter a terminal state.");
            main.SetGameplayInputEnabled(true);
            TestAssert.False(main.Player.CanAcceptGameplayInput,
                "Closing modal state restored gameplay after death.");
        });
    }
}
