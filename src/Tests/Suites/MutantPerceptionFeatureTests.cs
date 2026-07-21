using System;
using System.Threading.Tasks;
using Godot;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;
using LineZero.World2D.Enemies;

namespace LineZero.Tests.Suites;

public sealed class MutantPerceptionFeatureTests : IFeatureTestSuite
{
    public string Id => "mutant-perception";

    public string Description => "FOV, sight timing, low-FPS catch-up, and dead-target rules";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("brief-fov-crossing-detects-at-normal-and-low-fps", async () =>
        {
            double normalDetection = await SimulateBriefCrossing(context, 1.0 / 60.0);
            double lowDetection = await SimulateBriefCrossing(context, 0.2);

            TestAssert.True(double.IsFinite(normalDetection),
                "Normal-FPS brief FOV crossing was not detected.");
            TestAssert.True(double.IsFinite(lowDetection),
                "Low-FPS brief FOV crossing was not detected.");
            TestAssert.True(Math.Abs(normalDetection - lowDetection) <= 0.21,
                "Low-FPS detection timing drift exceeded one low-FPS frame.");
        });

        await context.RunAsync("direct-sight-overrides-idle-state", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "PerceptionTestRoot" });
            MutantController2D mutant = InstantiateMutant(root);
            TestPerceptionTarget2D target = new()
            {
                Name = "Target",
                Position = new Vector2(-100.0f, 0.0f),
            };
            root.AddChild(target);
            await context.WaitPhysicsFramesAsync(2);
            mutant.SetPhysicsProcess(false);
            mutant.BindTarget(target, target, target);

            mutant._PhysicsProcess(0.01);

            TestAssert.True(mutant.CanCurrentlySeeTarget,
                "Mutant did not acquire direct sight.");
            TestAssert.True(
                mutant.State is LineZero.Gameplay.Enemies.MutantState.Chase or
                    LineZero.Gameplay.Enemies.MutantState.Attack,
                "Direct sight did not override the previous state.");

            target.Health.ApplyDamage(new LineZero.Gameplay.Health.DamageInfo(100));
            mutant._PhysicsProcess(0.2);
            TestAssert.False(mutant.CanCurrentlySeeTarget,
                "Mutant continued seeing a dead player.");
            await context.DisposeNodeAsync(root);
        });
    }

    private static async Task<double> SimulateBriefCrossing(
        FeatureTestContext context,
        double delta)
    {
        Node2D root = context.AddNode(new Node2D { Name = "PerceptionSimulationRoot" });
        MutantController2D mutant = InstantiateMutant(root);
        TestPerceptionTarget2D target = new()
        {
            Name = "Target",
            Position = new Vector2(100.0f, 0.0f),
        };
        root.AddChild(target);
        await context.WaitPhysicsFramesAsync(2);
        mutant.SetPhysicsProcess(false);
        mutant.BindTarget(target, target, target);

        double detectionTime = double.PositiveInfinity;
        double elapsed = 0.0;
        while (elapsed < 1.0)
        {
            target.Position = elapsed >= 0.35 && elapsed < 0.55
                ? new Vector2(-100.0f, 0.0f)
                : new Vector2(100.0f, 0.0f);
            mutant._PhysicsProcess(delta);
            if (mutant.CanCurrentlySeeTarget && double.IsPositiveInfinity(detectionTime))
            {
                detectionTime = elapsed;
            }

            elapsed += delta;
        }

        await context.DisposeNodeAsync(root);
        return detectionTime;
    }

    private static MutantController2D InstantiateMutant(Node parent)
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://scenes/enemies/TunnelMutant2D.tscn")
            ?? throw new InvalidOperationException("Could not load mutant scene.");
        MutantController2D mutant = scene.Instantiate<MutantController2D>();
        mutant.Name = "Mutant";
        parent.AddChild(mutant);
        return mutant;
    }
}
