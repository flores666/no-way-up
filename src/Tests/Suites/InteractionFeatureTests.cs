using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Interaction;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;
using LineZero.World2D.Interaction;
using LineZero.World2D.Noise;

namespace LineZero.Tests.Suites;

public sealed class InteractionFeatureTests : IFeatureTestSuite
{
    public string Id => "interactions";

    public string Description => "Loot access, first-search noise, failed actors, and repeated access";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("container-emits-noise-only-on-first-valid-open", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "ContainerTestRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            TestHealthInventoryActorNode actor = new() { Name = "InventoryActor" };
            PackedScene scene = ResourceLoader.Load<PackedScene>(
                "res://scenes/interactables/LootContainer2D.tscn")
                ?? throw new System.InvalidOperationException("Could not load container scene.");
            LootContainer2D container = scene.Instantiate<LootContainer2D>();
            root.AddChild(noiseSystem);
            root.AddChild(actor);
            root.AddChild(container);
            await context.WaitProcessFramesAsync(2);
            container.BindNoiseSystem(noiseSystem);
            int emitted = 0;
            noiseSystem.NoiseEmitted += _ => emitted++;
            InteractionContext interaction = new(actor);

            container.Interact(interaction);
            await context.WaitProcessFramesAsync();
            container.Interact(interaction);
            await context.WaitProcessFramesAsync();
            container.Interact(interaction);

            TestAssert.True(container.HasBeenSearched,
                "Container did not retain its searched state.");
            TestAssert.Equal(1, emitted,
                "Repeated container access emitted repeated interaction noise.");
            TestAssert.True(container.CanInteract(interaction),
                "Searched container stopped allowing inventory access.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("failed-actor-does-not-search-or-emit-noise", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "ContainerFailureRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            Node actor = new() { Name = "NonInventoryActor" };
            PackedScene scene = ResourceLoader.Load<PackedScene>(
                "res://scenes/interactables/LootContainer2D.tscn")
                ?? throw new System.InvalidOperationException("Could not load container scene.");
            LootContainer2D container = scene.Instantiate<LootContainer2D>();
            root.AddChild(noiseSystem);
            root.AddChild(actor);
            root.AddChild(container);
            await context.WaitProcessFramesAsync(2);
            container.BindNoiseSystem(noiseSystem);
            int emitted = 0;
            noiseSystem.NoiseEmitted += _ => emitted++;

            InteractionResult result = container.Interact(new InteractionContext(actor));

            TestAssert.False(container.HasBeenSearched,
                "Failed actor permanently searched the container.");
            TestAssert.Equal(0, emitted,
                "Failed container interaction emitted world noise.");
            TestAssert.True(!string.IsNullOrWhiteSpace(result.Message),
                "Failed container interaction returned no feedback.");

            await context.DisposeNodeAsync(root);
        });
    }
}
