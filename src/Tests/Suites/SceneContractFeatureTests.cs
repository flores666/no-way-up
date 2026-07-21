using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Data;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Items;
using LineZero.Tests.Framework;
using LineZero.UI;
using LineZero.World2D;
using LineZero.World2D.Enemies;
using LineZero.World2D.Hazards;
using LineZero.World2D.Interaction;
using LineZero.World2D.Items;
using LineZero.World2D.Levels;
using LineZero.World2D.Noise;
using LineZero.World2D.Perception;
using LineZero.World2D.Power;

namespace LineZero.Tests.Suites;

public sealed class SceneContractFeatureTests : IFeatureTestSuite
{
    private const double GeometryTolerance = 0.001;

    public string Id => "scene-contracts";

    public string Description =>
        "Serialized node references, authored resources, bounds, inputs, and Main smoke load";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("player-sensors-resolve-explicit-dependencies", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "PlayerContractRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem2D" };
            root.AddChild(noiseSystem);

            PlayerController2D player = InstantiateDetached<PlayerController2D>(
                "res://scenes/player/Player.tscn");
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            await context.WaitPhysicsFramesAsync(2);

            LightExposureSensor2D lightSensor =
                player.GetNode<LightExposureSensor2D>("LightExposureSensor2D");
            PlayerHazardSensor2D hazardSensor =
                player.GetNode<PlayerHazardSensor2D>("PlayerHazardSensor2D");
            PlayerObjectiveSensor2D objectiveSensor =
                player.GetNode<PlayerObjectiveSensor2D>("PlayerObjectiveSensor2D");

            TestAssert.True(
                lightSensor.TryGetVisibilityController(
                    out PlayerVisibilityController2D? visibilityController),
                "Light-exposure sensor did not resolve its explicit dependency.");
            TestAssert.Same(
                player.VisibilityController,
                visibilityController!,
                "Light-exposure sensor resolved the wrong visibility controller.");

            TestAssert.True(
                hazardSensor.TryGetHealth(out var health),
                "Hazard sensor did not resolve its explicit health dependency.");
            TestAssert.Same(
                player.Health,
                health!,
                "Hazard sensor resolved the wrong health model.");

            TestAssert.True(
                objectiveSensor.TryGetPlayer(out PlayerController2D? objectivePlayer),
                "Objective sensor did not resolve its explicit player dependency.");
            TestAssert.Same(
                player,
                objectivePlayer!,
                "Objective sensor resolved the wrong player.");

            AssertStablePassiveSensor(
                lightSensor,
                CollisionLayers2D.LightExposureSensor,
                "light-exposure");
            AssertStablePassiveSensor(
                hazardSensor,
                CollisionLayers2D.PlayerHazardSensor,
                "hazard");
            AssertStablePassiveSensor(
                objectiveSensor,
                CollisionLayers2D.PlayerObjectiveSensor,
                "objective");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("dark-overlay-and-gameplay-zone-share-world-bounds", async () =>
        {
            TestLevelController2D level = context.InstantiateScene<TestLevelController2D>(
                "res://scenes/levels/TestLevel.tscn");
            await context.WaitProcessFramesAsync(2);

            LightExposureZoneOverlay2D overlay = level.GetNode<LightExposureZoneOverlay2D>(
                "Floor/DarkMaintenanceOverlay");
            CollisionShape2D sourceShape = overlay.GetNode<CollisionShape2D>(
                overlay.SourceShapePath);
            RectangleShape2D rectangle = sourceShape.Shape as RectangleShape2D
                ?? throw new TestAssertionException(
                    "Dark-maintenance gameplay zone does not use RectangleShape2D.");

            Rect2 overlayBounds = CalculateWorldBounds(overlay);
            Rect2 gameplayBounds = CalculateWorldBounds(sourceShape, rectangle);

            AssertRectNearlyEqual(
                gameplayBounds,
                overlayBounds,
                "Dark overlay and gameplay zone bounds differ.");
            AssertRectNearlyEqual(
                new Rect2(-840.0f, -120.0f, 360.0f, 240.0f),
                gameplayBounds,
                "Dark-maintenance world bounds changed unexpectedly.");

            await context.DisposeNodeAsync(level);
        });

        await context.RunAsync("metro-level-greybox-contracts", async () =>
        {
            MetroLevelController2D level = context.InstantiateScene<MetroLevelController2D>(
                "res://scenes/levels/MetroLevel01.tscn");
            await context.WaitProcessFramesAsync(2);
            await context.WaitPhysicsFramesAsync(2);

            TestAssert.True(level.Mutants.Count >= 3,
                "MetroLevel01 requires at least three authored mutants.");
            TestAssert.True(level.PoweredLights.Count >= 3,
                "MetroLevel01 requires multiple powered route lights.");
            TestAssert.True(level.EmergencyExitDoor.RequiresPower,
                "Metro emergency exit is not power-gated.");

            Node hazards = level.GetNode<Node>("Hazards");
            Node interactions = level.GetNode<Node>("Interactions");
            TestAssert.True(CountDirectChildren<DamageZone2D>(hazards) >= 2,
                "MetroLevel01 requires at least two authored hazards.");
            TestAssert.True(CountDirectChildren<LootContainer2D>(interactions) >= 4,
                "MetroLevel01 requires at least four loot containers.");
            TestAssert.True(CountDirectChildren<WorldItemPickup2D>(interactions) >= 5,
                "MetroLevel01 requires authored standalone pickups.");

            CollisionShape2D ductTop = level.GetNode<CollisionShape2D>(
                "WallCollisions/CrawlDuctTop");
            CollisionShape2D ductBottom = level.GetNode<CollisionShape2D>(
                "WallCollisions/CrawlDuctBottom");
            RectangleShape2D topRectangle = ductTop.Shape as RectangleShape2D
                ?? throw new TestAssertionException("Crawl duct top is not rectangular.");
            RectangleShape2D bottomRectangle = ductBottom.Shape as RectangleShape2D
                ?? throw new TestAssertionException("Crawl duct bottom is not rectangular.");
            float crawlGap =
                (ductBottom.Position.Y - bottomRectangle.Size.Y * 0.5f) -
                (ductTop.Position.Y + topRectangle.Size.Y * 0.5f);

            PlayerController2D player = InstantiateDetached<PlayerController2D>(
                "res://scenes/player/Player.tscn");
            CapsuleShape2D normalShape =
                player.GetNode<CollisionShape2D>("NormalCollisionShape").Shape as CapsuleShape2D
                ?? throw new TestAssertionException("Normal movement shape is not a capsule.");
            CapsuleShape2D crawlShape =
                player.GetNode<CollisionShape2D>("CrawlCollisionShape").Shape as CapsuleShape2D
                ?? throw new TestAssertionException("Crawl movement shape is not a capsule.");
            TestAssert.True(crawlGap < normalShape.Radius * 2.0f,
                "Normal movement collider can fit through the crawl-only passage.");
            TestAssert.True(crawlGap > crawlShape.Radius * 2.0f,
                "Crawl collider cannot fit through the authored maintenance passage.");
            player.Free();

            for (int index = 0; index < level.PoweredLights.Count; index++)
            {
                PowerControlledLight2D poweredLight = level.PoweredLights[index];
                poweredLight.BindPowerCircuit(level.PowerCircuit.Model);
                TestAssert.False(
                    poweredLight.GetNode<PointLight2D>("PoweredLight").Enabled,
                    "Powered metro light started online before fuse restoration.");
            }

            TestAssert.True(level.PowerCircuit.Model.TryInstallFuse(),
                "Metro power circuit rejected its first fuse installation.");
            for (int index = 0; index < level.PoweredLights.Count; index++)
            {
                TestAssert.True(
                    level.PoweredLights[index].GetNode<PointLight2D>("PoweredLight").Enabled,
                    "Metro route light did not switch on after power restoration.");
            }

            AssertNoHumanNpcNames(level);
            await context.DisposeNodeAsync(level);
        });

        await context.RunAsync("metro-landmarks-use-distinct-world-anchors", async () =>
        {
            MetroLevelController2D level = context.InstantiateScene<MetroLevelController2D>(
                "res://scenes/levels/MetroLevel01.tscn");
            await context.WaitProcessFramesAsync(2);

            Node2D landmarks = level.GetNode<Node2D>("Landmarks");
            string[] anchorNames =
            {
                "PlatformSignAnchor",
                "TicketHallSignAnchor",
                "ServiceSignAnchor",
                "ElectricalSignAnchor",
                "ExitSignAnchor",
                "TrainLineSignAnchor",
                "CrawlMarkingAnchor",
            };

            HashSet<Vector2> authoredPositions = new();
            for (int index = 0; index < anchorNames.Length; index++)
            {
                Node2D anchor = landmarks.GetNode<Node2D>(anchorNames[index]);
                TestAssert.True(
                    authoredPositions.Add(anchor.Position),
                    $"Metro landmark anchor '{anchor.Name}' overlaps another anchor.");
                TestAssert.Equal(
                    1,
                    anchor.GetChildCount(),
                    $"Metro landmark anchor '{anchor.Name}' must own one label.");
                TestAssert.True(
                    anchor.GetChild(0) is Label,
                    $"Metro landmark anchor '{anchor.Name}' does not own a Label.");
            }

            await context.DisposeNodeAsync(level);
        });

        await context.RunAsync("metro-mutant-debug-health-labels-stay-hidden", async () =>
        {
            MetroLevelController2D level = context.InstantiateScene<MetroLevelController2D>(
                "res://scenes/levels/MetroLevel01.tscn");
            await context.WaitProcessFramesAsync(2);

            TestAssert.True(level.Mutants.Count > 0,
                "MetroLevel01 has no mutant for debug-label validation.");
            MutantController2D mutant = level.Mutants[0];
            Label healthLabel = mutant.GetNode<Label>("MutantHealthLabel");
            TestAssert.False(mutant.EnableDebugHealthLabel,
                "Metro mutant health debug labels are enabled in gameplay.");
            TestAssert.False(healthLabel.Visible,
                "Metro mutant health label is visible before damage.");

            mutant.Health.ApplyDamage(new DamageInfo(mutant.Health.MaxHealth));
            TestAssert.False(healthLabel.Visible,
                "Dead metro mutant exposed the technical 'MUTANT DEAD' label.");

            await context.DisposeNodeAsync(level);
        });

        await context.RunAsync("gameplay-hud-is-compact-and-debug-free", async () =>
        {
            Main main = context.InstantiateScene<Main>("res://scenes/main/Main.tscn");
            await context.WaitProcessFramesAsync(3);

            DebugHud debugHud = main.GetNode<DebugHud>("%DebugHud");
            TestAssert.False(main.EnableDebugHud,
                "Gameplay Main unexpectedly enables the technical debug HUD.");
            TestAssert.False(debugHud.Visible,
                "Technical FPS/position HUD is visible in gameplay.");
            TestAssert.False(debugHud.IsProcessing(),
                "Hidden technical HUD still performs per-frame work.");

            Control[] leftStack =
            {
                main.GetNode<Control>("%HealthHud"),
                main.GetNode<Control>("%WeaponHud"),
                main.GetNode<Control>("%NoiseHud"),
                main.GetNode<Control>("%StaminaHud"),
                main.GetNode<Control>("%FlashlightHud"),
            };

            float previousBottom = 0.0f;
            for (int index = 0; index < leftStack.Length; index++)
            {
                Control panel = leftStack[index];
                TestAssert.True(panel.Size.X <= 224.0f + 0.01f,
                    $"HUD panel '{panel.Name}' is wider than the compact layout contract.");
                TestAssert.True(panel.Position.Y >= previousBottom,
                    $"HUD panel '{panel.Name}' overlaps the previous panel.");
                previousBottom = panel.Position.Y + panel.Size.Y;
            }

            TestAssert.True(previousBottom <= 350.0f + 0.01f,
                "Left HUD stack occupies too much vertical gameplay space.");
            TestAssert.True(main.GetNode<Control>("%VisibilityHud").Size.X <= 242.0f + 0.01f,
                "Visibility HUD is wider than the compact layout contract.");
            TestAssert.True(main.GetNode<Control>("%ObjectiveHud").Size.X <= 340.0f + 0.01f,
                "Objective HUD is wider than the compact layout contract.");

            await context.DisposeNodeAsync(main);
        });

        context.Run("authored-resources-load-and-validate", () =>
        {
            PlayerMovementSettings movement = LoadResource<PlayerMovementSettings>(
                "res://data/player/DefaultPlayerMovement.tres");
            movement.Validate();

            FirearmDefinition weapon = LoadResource<FirearmDefinition>(
                "res://data/weapons/ServicePistol.tres");
            weapon.Validate();

            FlashlightDefinition flashlight = LoadResource<FlashlightDefinition>(
                "res://data/flashlight/StandardFlashlight.tres");
            flashlight.Validate();

            ItemDefinition battery = LoadResource<ItemDefinition>(
                "res://data/items/Battery.tres");
            ItemDefinition replacementFuse = LoadResource<ItemDefinition>(
                "res://data/items/ReplacementFuse.tres");
            TestAssert.Equal("battery", battery.Id,
                "Battery resource stable ID changed.");
            TestAssert.Equal("replacement_fuse", replacementFuse.Id,
                "Replacement fuse stable ID changed.");
            TestAssert.Equal(1, replacementFuse.MaxStackSize,
                "Replacement fuse must remain a non-stackable objective item.");
        });


        context.Run("exit-zone-uses-objective-sensor-layer-only", () =>
        {
            ObjectiveExitZone2D zone = InstantiateDetached<ObjectiveExitZone2D>(
                "res://scenes/levels/ObjectiveExitZone2D.tscn");
            TestAssert.Equal(0U, zone.CollisionLayer,
                "Exit zone unexpectedly occupies a collision layer.");
            TestAssert.Equal(
                CollisionLayers2D.PlayerObjectiveSensor,
                zone.CollisionMask,
                "Exit zone can be activated by something other than the objective sensor.");
            zone.Free();
        });

        context.Run("required-input-actions-exist", () =>
        {
            string[] requiredActions =
            {
                "move_up",
                "move_down",
                "move_left",
                "move_right",
                "sprint",
                "crouch",
                "crawl",
                "toggle_flashlight",
                "replace_battery",
                "interact",
                "toggle_inventory",
                "fire",
                "reload",
            };

            for (int index = 0; index < requiredActions.Length; index++)
            {
                string action = requiredActions[index];
                TestAssert.True(
                    InputMap.HasAction(action),
                    $"Required Input Map action '{action}' is missing.");
            }
        });

        await context.RunAsync("main-scene-composition-smoke-loads", async () =>
        {
            Main main = context.InstantiateScene<Main>("res://scenes/main/Main.tscn");
            await context.WaitProcessFramesAsync(3);
            await context.WaitPhysicsFramesAsync(2);

            TestAssert.True(
                main.IsInitialized,
                "Main did not finish composition-root initialization.");
            TestAssert.True(
                main.GetNodeOrNull<PlayerController2D>("%Player") is not null,
                "Main smoke scene did not contain the unique Player node.");
            TestAssert.True(
                main.GetNodeOrNull<MetroLevelController2D>("%PlayableLevel") is not null,
                "Main does not load MetroLevel01 as the default gameplay level.");
            TestAssert.True(
                main.GetNodeOrNull<NoiseSystem2D>("%NoiseSystem2D") is not null,
                "Main smoke scene did not contain the unique NoiseSystem2D node.");

            await context.DisposeNodeAsync(main);
        });

        await context.RunAsync("technical-test-level-remains-runnable", async () =>
        {
            Main main = context.InstantiateScene<Main>("res://scenes/main/TestMain.tscn");
            await context.WaitProcessFramesAsync(3);
            await context.WaitPhysicsFramesAsync(2);

            TestAssert.True(main.IsInitialized,
                "TestMain did not initialize the technical regression level.");
            TestAssert.True(main.EnableDebugHud,
                "TestMain no longer enables the technical debug HUD.");
            TestAssert.True(main.GetNode<DebugHud>("%DebugHud").Visible,
                "TestMain debug HUD is not visible.");
            TestAssert.True(
                main.GetNodeOrNull<TestLevelController2D>("%PlayableLevel") is not null,
                "TestMain no longer contains TestLevel.");

            await context.DisposeNodeAsync(main);
        });
    }

    private static int CountDirectChildren<TNode>(Node parent)
        where TNode : Node
    {
        int count = 0;
        for (int index = 0; index < parent.GetChildCount(); index++)
        {
            if (parent.GetChild(index) is TNode)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertNoHumanNpcNames(Node root)
    {
        string[] bannedTokens =
        {
            "survivor",
            "civilian",
            "companion",
            "human_npc",
            "dialogue",
            "cutscene",
        };

        Stack<Node> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            Node current = pending.Pop();
            string normalizedName = current.Name.ToString().ToLowerInvariant();
            for (int index = 0; index < bannedTokens.Length; index++)
            {
                TestAssert.False(
                    normalizedName.Contains(bannedTokens[index], StringComparison.Ordinal),
                    $"MetroLevel01 contains prohibited human/NPC content at '{current.GetPath()}'.");
            }

            for (int index = 0; index < current.GetChildCount(); index++)
            {
                pending.Push(current.GetChild(index));
            }
        }
    }

    private static T InstantiateDetached<T>(string resourcePath)
        where T : Node
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(resourcePath)
            ?? throw new InvalidOperationException(
                $"Could not load test scene '{resourcePath}'.");
        return scene.Instantiate<T>();
    }

    private static T LoadResource<T>(string resourcePath)
        where T : Resource
    {
        return ResourceLoader.Load<T>(resourcePath)
            ?? throw new TestAssertionException(
                $"Could not load required resource '{resourcePath}'.");
    }

    private static void AssertStablePassiveSensor(
        Area2D sensor,
        uint expectedLayer,
        string sensorName)
    {
        TestAssert.Equal(
            expectedLayer,
            sensor.CollisionLayer,
            $"The {sensorName} sensor uses the wrong collision layer.");
        TestAssert.Equal(
            0U,
            sensor.CollisionMask,
            $"The {sensorName} sensor must not query physics objects itself.");
        TestAssert.False(
            sensor.Monitoring,
            $"The {sensorName} sensor must remain passive.");
        TestAssert.True(
            sensor.Monitorable,
            $"The {sensorName} sensor must remain detectable by authored zones.");

        CollisionShape2D? collisionShape = null;
        for (int index = 0; index < sensor.GetChildCount(); index++)
        {
            if (sensor.GetChild(index) is CollisionShape2D candidate)
            {
                collisionShape = candidate;
                break;
            }
        }

        TestAssert.True(
            collisionShape is not null &&
            collisionShape.Shape is not null &&
            !collisionShape.Disabled,
            $"The {sensorName} sensor requires one enabled constant collision shape.");
    }

    private static Rect2 CalculateWorldBounds(Polygon2D polygon)
    {
        Vector2[] points = polygon.Polygon;
        if (points.Length == 0)
        {
            throw new TestAssertionException("Overlay polygon has no points.");
        }

        Vector2 first = polygon.ToGlobal(points[0]);
        float minX = first.X;
        float maxX = first.X;
        float minY = first.Y;
        float maxY = first.Y;
        for (int index = 1; index < points.Length; index++)
        {
            Vector2 point = polygon.ToGlobal(points[index]);
            minX = MathF.Min(minX, point.X);
            maxX = MathF.Max(maxX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxY = MathF.Max(maxY, point.Y);
        }

        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private static Rect2 CalculateWorldBounds(
        CollisionShape2D collisionShape,
        RectangleShape2D rectangle)
    {
        Vector2 halfSize = rectangle.Size * 0.5f;
        Vector2[] localCorners =
        {
            new(-halfSize.X, -halfSize.Y),
            new(halfSize.X, -halfSize.Y),
            new(halfSize.X, halfSize.Y),
            new(-halfSize.X, halfSize.Y),
        };

        Vector2 first = collisionShape.ToGlobal(localCorners[0]);
        float minX = first.X;
        float maxX = first.X;
        float minY = first.Y;
        float maxY = first.Y;
        for (int index = 1; index < localCorners.Length; index++)
        {
            Vector2 point = collisionShape.ToGlobal(localCorners[index]);
            minX = MathF.Min(minX, point.X);
            maxX = MathF.Max(maxX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxY = MathF.Max(maxY, point.Y);
        }

        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private static void AssertRectNearlyEqual(
        Rect2 expected,
        Rect2 actual,
        string message)
    {
        TestAssert.NearlyEqual(expected.Position.X, actual.Position.X, GeometryTolerance, message);
        TestAssert.NearlyEqual(expected.Position.Y, actual.Position.Y, GeometryTolerance, message);
        TestAssert.NearlyEqual(expected.Size.X, actual.Size.X, GeometryTolerance, message);
        TestAssert.NearlyEqual(expected.Size.Y, actual.Size.Y, GeometryTolerance, message);
    }
}
