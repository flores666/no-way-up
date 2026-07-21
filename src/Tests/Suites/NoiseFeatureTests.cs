using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Noise;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Noise;

namespace LineZero.Tests.Suites;

public sealed class NoiseFeatureTests : IFeatureTestSuite
{
    public string Id => "noise-hearing";

    public string Description => "Distance, sensitivity, multi-wall attenuation, and deduplication";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("multiple-wall-shapes-attenuate-independently", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "NoiseTestRoot" });
            NoiseSystem2D noiseSystem = new()
            {
                Name = "NoiseSystem",
                FootstepBaseRadius = 130.0f,
                GunshotBaseRadius = 650.0f,
                WallAttenuation = 0.5f,
                OcclusionCollisionMask = CollisionLayers2D.World,
            };
            root.AddChild(noiseSystem);
            Node2D source = new() { Name = "Source", Position = Vector2.Zero };
            TestNoiseListener2D listener = new()
            {
                Name = "Listener",
                Position = new Vector2(80.0f, 0.0f),
                HearingSensitivity = 1.0f,
                MinimumAudibleIntensity = 0.01f,
            };
            root.AddChild(source);
            root.AddChild(listener);
            StaticBody2D compoundWall = CreateCompoundWall();
            root.AddChild(compoundWall);
            await context.WaitPhysicsFramesAsync(2);
            noiseSystem.RegisterListener(listener);

            noiseSystem.EmitNoise(
                source,
                NoiseKind.Footstep,
                1.0f,
                source.GlobalPosition,
                description: "two-wall footstep");
            TestAssert.Equal(0, listener.ReceivedCount,
                "Footstep remained audible through two walls at 80 units.");

            noiseSystem.EmitNoise(
                source,
                NoiseKind.Gunshot,
                1.0f,
                source.GlobalPosition,
                description: "two-wall gunshot");
            TestAssert.Equal(1, listener.ReceivedCount,
                "Nearby gunshot was completely lost through two walls.");
            TestAssert.True(listener.LastNoise is not null,
                "Delivered gunshot did not provide perceived noise.");
            TestAssert.NearlyEqual(0.25, listener.LastNoise!.PerceivedIntensity, 1e-6,
                "Two distinct wall shapes did not apply attenuation twice.");
            TestAssert.True(listener.LastNoise.WasOccluded,
                "Occluded gunshot was not marked occluded.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("distance-and-sensitivity-remain-active", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "NoiseDistanceRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            Node2D source = new() { Name = "Source", Position = Vector2.Zero };
            TestNoiseListener2D listener = new()
            {
                Name = "Listener",
                Position = new Vector2(150.0f, 0.0f),
                HearingSensitivity = 1.0f,
                MinimumAudibleIntensity = 0.01f,
            };
            root.AddChild(noiseSystem);
            root.AddChild(source);
            root.AddChild(listener);
            await context.WaitPhysicsFramesAsync(1);
            noiseSystem.RegisterListener(listener);

            noiseSystem.EmitNoise(
                source,
                NoiseKind.Footstep,
                1.0f,
                source.GlobalPosition,
                description: "out-of-range");
            TestAssert.Equal(0, listener.ReceivedCount,
                "Direct-distance prefilter was bypassed.");

            listener.HearingSensitivity = 2.0f;
            noiseSystem.EmitNoise(
                source,
                NoiseKind.Footstep,
                1.0f,
                source.GlobalPosition,
                description: "sensitive-listener");
            TestAssert.Equal(1, listener.ReceivedCount,
                "Hearing sensitivity no longer expanded effective radius.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("same-frame-identical-emission-is-deduplicated", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "NoiseDedupRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            Node2D source = new() { Name = "Source" };
            root.AddChild(noiseSystem);
            root.AddChild(source);
            await context.WaitProcessFramesAsync();
            int emitted = 0;
            noiseSystem.NoiseEmitted += _ => emitted++;

            NoiseOccurrence2D first = noiseSystem.EmitNoise(
                source,
                NoiseKind.Interaction,
                1.0f,
                Vector2.Zero,
                description: "same");
            NoiseOccurrence2D duplicate = noiseSystem.EmitNoise(
                source,
                NoiseKind.Interaction,
                1.0f,
                Vector2.Zero,
                description: "same");

            TestAssert.Same(first, duplicate,
                "Same-frame duplicate did not return the original occurrence.");
            TestAssert.Equal(1, emitted,
                "Same-frame duplicate published a second noise event.");
            await context.DisposeNodeAsync(root);
        });
    }

    private static StaticBody2D CreateCompoundWall()
    {
        StaticBody2D body = new()
        {
            Name = "CompoundWall",
            CollisionLayer = CollisionLayers2D.World,
            CollisionMask = 0,
        };
        body.AddChild(CreateWallShape(new Vector2(30.0f, 0.0f)));
        body.AddChild(CreateWallShape(new Vector2(60.0f, 0.0f)));
        return body;
    }

    private static CollisionShape2D CreateWallShape(Vector2 position)
    {
        return new CollisionShape2D
        {
            Position = position,
            Shape = new RectangleShape2D { Size = new Vector2(2.0f, 100.0f) },
        };
    }
}
