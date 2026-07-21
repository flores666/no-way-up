using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Noise;

namespace LineZero.Tests.Suites;

public sealed class FootstepFeatureTests : IFeatureTestSuite
{
    public string Id => "footsteps";

    public string Description => "Distance accumulation, pending debt, FPS equivalence, and reset safety";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("equivalent-distance-produces-equivalent-step-count", async () =>
        {
            FootstepRun normal = await Simulate(
                context,
                new[] { 110.0f, 110.0f, 110.0f, 110.0f, 110.0f, 110.0f },
                delta: 0.2);
            FootstepRun lowFps = await Simulate(
                context,
                new[] { 660.0f },
                delta: 0.8);

            TestAssert.Equal(6, normal.Intensities.Count,
                "Normal-FPS distance produced the wrong number of steps.");
            TestAssert.Equal(normal.Intensities.Count, lowFps.Intensities.Count,
                "Low-FPS movement lost completed footsteps.");
            for (int index = 0; index < lowFps.Intensities.Count; index++)
            {
                TestAssert.True(lowFps.Intensities[index] <= lowFps.SprintMaximum,
                    "Pending footstep exceeded Sprint intensity.");
            }
        });

        await context.RunAsync("no-more-than-three-events-per-update", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "FootstepBudgetRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            await context.WaitPhysicsFramesAsync(2);
            PlayerFootstepNoiseEmitter2D emitter = player.GetNode<PlayerFootstepNoiseEmitter2D>(
                "%PlayerFootstepNoiseEmitter2D");
            emitter.SetPhysicsProcess(false);
            int events = 0;
            emitter.FootstepEmitted += _ => events++;

            player.GlobalPosition += new Vector2(660.0f, 0.0f);
            emitter._PhysicsProcess(0.8);
            TestAssert.Equal(3, events,
                "One physics update emitted more or fewer than three due footsteps.");
            emitter._PhysicsProcess(0.1);
            TestAssert.Equal(6, events,
                "Pending footstep debt was not emitted on the next update.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("disabled-gameplay-clears-pending-debt", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "FootstepResetRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            await context.WaitPhysicsFramesAsync(2);
            PlayerFootstepNoiseEmitter2D emitter = player.GetNode<PlayerFootstepNoiseEmitter2D>(
                "%PlayerFootstepNoiseEmitter2D");
            emitter.SetPhysicsProcess(false);
            int events = 0;
            emitter.FootstepEmitted += _ => events++;

            player.GlobalPosition += new Vector2(660.0f, 0.0f);
            emitter._PhysicsProcess(0.8);
            TestAssert.Equal(3, events, "Setup did not create pending step debt.");
            player.SetGameplayInputEnabled(false);
            emitter._PhysicsProcess(0.1);
            player.SetGameplayInputEnabled(true);
            emitter._PhysicsProcess(0.1);

            TestAssert.Equal(3, events,
                "Disabled gameplay emitted or retained pending footsteps.");
            await context.DisposeNodeAsync(root);
        });
    }

    private static async Task<FootstepRun> Simulate(
        FeatureTestContext context,
        IReadOnlyList<float> distances,
        double delta)
    {
        Node2D root = context.AddNode(new Node2D { Name = "FootstepSimulationRoot" });
        NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
        PlayerController2D player = LoadPlayer();
        root.AddChild(noiseSystem);
        root.AddChild(player);
        player.BindNoiseSystem(noiseSystem);
        await context.WaitPhysicsFramesAsync(2);
        PlayerFootstepNoiseEmitter2D emitter = player.GetNode<PlayerFootstepNoiseEmitter2D>(
            "%PlayerFootstepNoiseEmitter2D");
        emitter.SetPhysicsProcess(false);
        List<float> intensities = new();
        emitter.FootstepEmitted += occurrence =>
            intensities.Add(occurrence.Noise.Intensity);

        for (int index = 0; index < distances.Count; index++)
        {
            player.GlobalPosition += new Vector2(distances[index], 0.0f);
            emitter._PhysicsProcess(delta);
        }

        for (int flush = 0; flush < 16; flush++)
        {
            int before = intensities.Count;
            emitter._PhysicsProcess(0.1);
            if (intensities.Count == before)
            {
                break;
            }
        }

        FootstepRun run = new(intensities, emitter.SprintFootstepIntensity);
        await context.DisposeNodeAsync(root);
        return run;
    }

    private static PlayerController2D LoadPlayer()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>("res://scenes/player/Player.tscn")
            ?? throw new System.InvalidOperationException("Could not load player scene.");
        return scene.Instantiate<PlayerController2D>();
    }

    private readonly record struct FootstepRun(
        IReadOnlyList<float> Intensities,
        float SprintMaximum);
}
