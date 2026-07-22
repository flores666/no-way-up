using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Noise;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;
using LineZero.UI;
using LineZero.World3D;
using LineZero.World3D.Flashlight;
using LineZero.World3D.Interaction;
using LineZero.World3D.Noise;
using LineZero.World3D.Perception;

namespace LineZero.Tests.Suites;

public sealed class World3DStealthFeatureTests : IFeatureTestSuite
{
    public string Id => "world-3d-stealth";

    public string Description =>
        "3D flashlight, deterministic visibility, distance noise, and attenuation";

    public async Task RunAsync(FeatureTestContext context)
    {
        context.Run("footstep-distance-debt-survives-bounded-drain", () =>
        {
            FootstepCadenceModel cadence = new();
            FootstepCadenceAdvanceResult advance = cadence.Advance(
                travelledDistance: 10.0f,
                stepDistance: 1.0f,
                intensity: 1.8f);
            TestAssert.Equal(10L, advance.CompletedSteps,
                "Long-frame distance produced the wrong completed-step count.");

            for (int index = 0; index < 4; index++)
            {
                TestAssert.True(cadence.TryTakePendingStep(out float intensity),
                    "Bounded footstep drain lost pending debt.");
                TestAssert.NearlyEqual(1.8, intensity, 0.000001,
                    "Pending step changed its emitted intensity.");
            }

            TestAssert.Equal(6L, cadence.PendingSteps,
                "The per-update emission bound discarded remaining step debt.");
            int remaining = 0;
            while (cadence.TryTakePendingStep(out _))
            {
                remaining++;
            }

            TestAssert.Equal(6, remaining,
                "Preserved step debt could not be drained later.");

            cadence.Advance(0.5f, 1.0f, 1.8f);
            cadence.Advance(0.5f, 1.0f, 0.2f);
            TestAssert.True(cadence.TryTakePendingStep(out float mixedIntensity),
                "Mixed-mode distance did not complete one footstep.");
            TestAssert.NearlyEqual(1.0, mixedIntensity, 0.000001,
                "Mixed-mode footstep did not retain actual intensity contribution.");
        });

        await context.RunAsync(
            "main3d-flashlight-and-constant-visibility-sensor",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(8);
                PlayerFlashlightController3D flashlight =
                    main.Player.GetNode<PlayerFlashlightController3D>(
                        "%PlayerFlashlightController3D");
                PlayerVisibilityController3D visibility =
                    main.Player.GetNode<PlayerVisibilityController3D>(
                        "%PlayerVisibilityController3D");
                PlayerVisibilitySensor3D sensor =
                    main.Player.GetNode<PlayerVisibilitySensor3D>(
                        "%PlayerVisibilitySensor3D");
                CollisionShape3D sensorShape =
                    sensor.GetNode<CollisionShape3D>(
                        "%PlayerVisibilitySensorShape3D");
                Shape3D shapeBefore = sensorShape.Shape;
                Transform3D transformBefore = sensorShape.Transform;

                flashlight.SetPhysicsProcess(false);
                double chargeBefore = flashlight.Model.CurrentCharge;
                flashlight._PhysicsProcess(2.0);
                TestAssert.NearlyEqual(
                    chargeBefore - 2.0,
                    flashlight.Model.CurrentCharge,
                    0.000001,
                    "3D flashlight did not drain by elapsed time.");

                main.Player.GlobalPosition = new Vector3(-10.0f, 0.05f, -8.0f);
                main.Player.Velocity = Vector3.Zero;
                await context.WaitPhysicsFramesAsync(4);
                TestAssert.NearlyEqual(
                    0.45,
                    visibility.State.AmbientLightMultiplier,
                    0.000001,
                    "Dark Area3D did not update deterministic visibility.");

                TestAssert.True(main.Player.TrySetPosture(MovementMode.Crawl),
                    "Player could not enter Crawl in the dark-zone fixture.");
                TestAssert.Same(shapeBefore, sensorShape.Shape,
                    "Crawl replaced the constant visibility sensor shape.");
                TestAssert.Equal(transformBefore, sensorShape.Transform,
                    "Crawl moved or resized the visibility sensor.");
                TestAssert.NearlyEqual(
                    0.40,
                    visibility.State.PostureMultiplier,
                    0.000001,
                    "Crawl posture did not contribute to visibility.");

                main.Player.GlobalPosition = new Vector3(10.0f, 0.05f, -8.0f);
                main.Player.Velocity = Vector3.Zero;
                await context.WaitPhysicsFramesAsync(4);
                TestAssert.NearlyEqual(
                    1.4,
                    visibility.State.AmbientLightMultiplier,
                    0.000001,
                    "Bright Area3D did not update deterministic visibility.");
            });

        await context.RunAsync("noise-hud-uses-actual-3d-occurrence", async () =>
        {
            Main3D main = context.InstantiateScene<Main3D>(
                "res://scenes/3d/Main3D.tscn");
            await context.WaitPhysicsFramesAsync(4);
            NoiseSystem3D noiseSystem = main.GetNode<NoiseSystem3D>("%NoiseSystem3D");
            NoiseHudController hud = main.GetNode<NoiseHudController>("%NoiseHud");
            Label label = hud.GetNode<Label>("%NoiseLabel");

            noiseSystem.EmitNoise(
                main.Player,
                NoiseKind.Interaction,
                1.0f,
                main.Player.GlobalPosition,
                main.Player,
                "Quiet 3D interaction");
            TestAssert.Equal("NOISE: LOW", label.Text,
                "3D HUD ignored the emitted interaction intensity.");

            noiseSystem.EmitNoise(
                main.Player,
                NoiseKind.Gunshot,
                1.0f,
                main.Player.GlobalPosition,
                main.Player,
                "3D gunshot");
            TestAssert.Equal("NOISE: LOUD", label.Text,
                "3D HUD classified gunshot from unrelated current state.");

            LootContainer3D container =
                main.GetNode<Node3D>("%TestLevel3D")
                    .GetNode<LootContainer3D>("%EmergencyCabinet3D");
            int containerNoiseCount = 0;
            noiseSystem.NoiseEmitted += occurrence =>
            {
                if (occurrence.Noise.Kind == NoiseKind.Interaction &&
                    occurrence.Noise.Description is not null &&
                    occurrence.Noise.Description.StartsWith("Searching "))
                {
                    containerNoiseCount++;
                }
            };
            container.Interact(new InteractionContext(main.Player));
            container.Interact(new InteractionContext(main.Player));
            TestAssert.Equal(1, containerNoiseCount,
                "Searching one container emitted duplicate logical noise.");
        });

        await context.RunAsync(
            "multiple-3d-walls-attenuate-and-listeners-are-isolated",
            async () =>
            {
                Node3D root = context.AddNode(new Node3D
                {
                    Name = "Noise3DTestRoot"
                });
                NoiseSystem3D noiseSystem = new()
                {
                    Name = "NoiseSystem3D",
                    WallAttenuation = 0.5f,
                    OcclusionCollisionMask = CollisionLayers3D.World
                };
                Node3D source = new()
                {
                    Name = "Source3D",
                    Position = new Vector3(0.0f, 1.0f, 0.0f)
                };
                TestNoiseListener3D healthy = new()
                {
                    Name = "HealthyListener3D",
                    Position = new Vector3(6.0f, 1.0f, 0.0f)
                };
                TestNoiseListener3D throwing = new()
                {
                    Name = "ThrowingListener3D",
                    Position = new Vector3(6.0f, 1.0f, 0.4f),
                    ThrowOnReceive = true
                };
                root.AddChild(noiseSystem);
                root.AddChild(source);
                root.AddChild(healthy);
                root.AddChild(throwing);
                root.AddChild(CreateWall("WallA3D", 2.0f));
                root.AddChild(CreateWall("WallB3D", 4.0f));
                await context.WaitPhysicsFramesAsync(2);
                noiseSystem.RegisterListener(healthy);
                noiseSystem.RegisterListener(throwing);

                noiseSystem.EmitNoise(
                    source,
                    NoiseKind.Gunshot,
                    2.0f,
                    source.GlobalPosition,
                    description: "Two-wall 3D gunshot");

                TestAssert.Equal(1, healthy.ReceivedCount,
                    "Throwing 3D listener blocked a healthy listener.");
                TestAssert.True(healthy.LastNoise is not null,
                    "Healthy 3D listener received no perceived occurrence.");
                TestAssert.Equal(2, healthy.LastNoise!.BarrierCount,
                    "Bounded 3D raycasts did not count both blocking walls.");
                TestAssert.NearlyEqual(
                    0.5,
                    healthy.LastNoise.PerceivedIntensity,
                    0.000001,
                    "Two 3D walls did not apply attenuation independently.");
            });
    }

    private static StaticBody3D CreateWall(string name, float xPosition)
    {
        StaticBody3D wall = new()
        {
            Name = name,
            Position = new Vector3(xPosition, 1.0f, 0.0f),
            CollisionLayer = CollisionLayers3D.World,
            CollisionMask = 0
        };
        wall.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(0.2f, 3.0f, 3.0f)
            }
        });
        return wall;
    }
}
