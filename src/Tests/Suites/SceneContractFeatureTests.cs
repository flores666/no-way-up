using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Data;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Items;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Hazards;
using LineZero.World2D.Levels;
using LineZero.World2D.Noise;
using LineZero.World2D.Perception;

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
                main.GetNodeOrNull<TestLevelController2D>("%TestLevel") is not null,
                "Main smoke scene did not contain the unique TestLevel node.");
            TestAssert.True(
                main.GetNodeOrNull<NoiseSystem2D>("%NoiseSystem2D") is not null,
                "Main smoke scene did not contain the unique NoiseSystem2D node.");

            await context.DisposeNodeAsync(main);
        });
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
