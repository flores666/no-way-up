using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Enemies;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Noise;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;
using LineZero.World2D.Enemies;
using LineZero.World2D.Noise;

namespace LineZero.Tests.Suites;

public sealed class MutantPerceptionFeatureTests : IFeatureTestSuite
{
    public string Id => "mutant-perception";

    public string Description =>
        "Stimulus priority, FOV, chase memory, hearing, and dead-target rules";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("chasing-mutant-hears-gunshot-and-remains-in-chase", async () =>
        {
            var scenario = await CreateScenario(context);
            AcquireChase(scenario.Mutant, scenario.Target);
            int stateChanges = 0;
            scenario.Mutant.StateChanged += (_, _) => stateChanges++;

            scenario.Mutant.ReceiveNoise(CreateNoise(
                scenario.Target,
                NoiseKind.Gunshot,
                1,
                new Vector2(-88.0f, 12.0f),
                1.0f));
            scenario.Mutant._PhysicsProcess(0.01);

            TestAssert.Equal(MutantState.Chase, scenario.Mutant.State,
                "A player gunshot downgraded an active chase.");
            TestAssert.Equal(0, stateChanges,
                "A Chase-to-Chase decision emitted StateChanged.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("repeated-player-damage-never-enters-investigate", async () =>
        {
            var scenario = await CreateScenario(context);
            AcquireChase(scenario.Mutant, scenario.Target);
            List<MutantState> enteredStates = new();
            scenario.Mutant.StateChanged += (_, next) => enteredStates.Add(next);

            for (ulong sequence = 1; sequence <= 3; sequence++)
            {
                scenario.Mutant.Health.ApplyDamage(new DamageInfo(
                    1,
                    scenario.Target,
                    "TestPlayerShot"));
                scenario.Mutant.ReceiveNoise(CreateNoise(
                    scenario.Target,
                    NoiseKind.Gunshot,
                    sequence,
                    scenario.Target.GlobalPosition,
                    1.0f));
                scenario.Mutant._PhysicsProcess(0.01);
            }

            TestAssert.Equal(MutantState.Chase, scenario.Mutant.State,
                "Repeated player damage did not preserve Chase.");
            TestAssert.False(enteredStates.Contains(MutantState.Investigate),
                "Being shot produced an intermediate Investigate state.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("player-damage-enters-chase-without-intermediate-state", async () =>
        {
            var scenario = await CreateScenario(context, withPatrol: true);
            List<MutantState> enteredStates = new();
            scenario.Mutant.StateChanged += (_, next) => enteredStates.Add(next);

            scenario.Mutant.Health.ApplyDamage(new DamageInfo(
                1,
                scenario.Target,
                "TestPlayerShot"));

            TestAssert.Equal(MutantState.Chase, scenario.Mutant.State,
                "Validated player damage did not enter Chase.");
            TestAssert.Equal(1, enteredStates.Count,
                "Player damage caused more than one state transition.");
            TestAssert.Equal(MutantState.Chase, enteredStates[0],
                "Player damage passed through a lower-priority state.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("same-frame-gunshots-resolve-once-by-priority", async () =>
        {
            var scenario = await CreateScenario(context, withPatrol: true);
            int stateChanges = 0;
            scenario.Mutant.StateChanged += (_, _) => stateChanges++;

            scenario.Mutant.ReceiveNoise(CreateNoise(
                scenario.Target,
                NoiseKind.Gunshot,
                1,
                new Vector2(20.0f, 0.0f),
                0.5f));
            scenario.Mutant.ReceiveNoise(CreateNoise(
                scenario.Target,
                NoiseKind.Gunshot,
                2,
                new Vector2(70.0f, 0.0f),
                2.0f));
            scenario.Mutant.ReceiveNoise(CreateNoise(
                scenario.Target,
                NoiseKind.Gunshot,
                3,
                new Vector2(40.0f, 0.0f),
                1.0f));
            scenario.Mutant._PhysicsProcess(0.01);

            TestAssert.Equal(MutantState.Investigate, scenario.Mutant.State,
                "A patrolling mutant did not investigate the strongest gunshot.");
            TestAssert.Equal(1, stateChanges,
                "Multiple same-frame gunshots caused repeated state changes.");
            TestAssert.True(
                scenario.Mutant.InvestigationPosition.IsEqualApprox(
                    new Vector2(70.0f, 0.0f)),
                "Same-frame gunshots did not resolve to the strongest stimulus.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("state-changed-is-not-emitted-for-chase-to-chase", async () =>
        {
            var scenario = await CreateScenario(context);
            AcquireChase(scenario.Mutant, scenario.Target);
            int stateChanges = 0;
            scenario.Mutant.StateChanged += (_, _) => stateChanges++;

            scenario.Mutant._PhysicsProcess(0.01);
            scenario.Mutant._PhysicsProcess(0.01);
            scenario.Mutant._PhysicsProcess(0.01);

            TestAssert.Equal(MutantState.Chase, scenario.Mutant.State,
                "Repeated chase decisions changed the effective state.");
            TestAssert.Equal(0, stateChanges,
                "StateChanged was emitted for Chase-to-Chase.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("gunshot-updates-memory-without-downgrading-chase", async () =>
        {
            var scenario = await CreateScenario(context);
            AcquireChase(scenario.Mutant, scenario.Target);
            HideTarget(scenario.Mutant, scenario.Target);
            int stateChanges = 0;
            scenario.Mutant.StateChanged += (_, _) => stateChanges++;
            Vector2 shotOrigin = new(76.0f, 14.0f);
            Node2D environmentalSource = AddNoiseSource(
                scenario.Root,
                "CompetingGunshot");

            scenario.Mutant.ReceiveNoise(CreateNoise(
                environmentalSource,
                NoiseKind.Gunshot,
                1,
                new Vector2(20.0f, 60.0f),
                3.0f));
            scenario.Mutant.ReceiveNoise(CreateNoise(
                scenario.Target,
                NoiseKind.Gunshot,
                2,
                shotOrigin,
                1.0f));
            scenario.Mutant._PhysicsProcess(
                scenario.Mutant.Definition!.LostTargetGraceSeconds * 0.5);

            TestAssert.Equal(MutantState.Chase, scenario.Mutant.State,
                "A target gunshot downgraded Chase during grace.");
            TestAssert.True(
                scenario.Mutant.LastKnownTargetPosition.IsEqualApprox(shotOrigin),
                "A target gunshot did not update last-known target memory.");
            TestAssert.Equal(0, stateChanges,
                "Updating chase memory emitted a redundant state transition.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("hidden-target-remains-chased-during-grace", async () =>
        {
            var scenario = await CreateScenario(context);
            AcquireChase(scenario.Mutant, scenario.Target);
            HideTarget(scenario.Mutant, scenario.Target);
            Node2D environmentalSource = AddNoiseSource(
                scenario.Root,
                "EnvironmentalNoise");
            Vector2 rememberedPosition = scenario.Mutant.LastKnownTargetPosition;

            scenario.Mutant.ReceiveNoise(CreateNoise(
                environmentalSource,
                NoiseKind.Interaction,
                1,
                new Vector2(45.0f, 35.0f),
                2.0f));
            scenario.Mutant._PhysicsProcess(
                scenario.Mutant.Definition!.LostTargetGraceSeconds - 0.1);

            TestAssert.Equal(MutantState.Chase, scenario.Mutant.State,
                "Lower-priority noise ended Chase before grace expired.");
            TestAssert.True(
                scenario.Mutant.LastKnownTargetPosition.IsEqualApprox(
                    rememberedPosition),
                "Ignored environmental noise overwrote target memory.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("expired-chase-memory-allows-new-investigation", async () =>
        {
            var scenario = await CreateScenario(context);
            AcquireChase(scenario.Mutant, scenario.Target);
            ExpireChaseGrace(scenario.Mutant, scenario.Target);
            scenario.Mutant._PhysicsProcess(
                scenario.Mutant.Definition!.MaximumSearchSeconds + 0.1);
            TestAssert.Equal(MutantState.Idle, scenario.Mutant.State,
                "Expired chase search did not return to default behavior.");

            Node2D source = AddNoiseSource(scenario.Root, "UnconfirmedGunshot");
            scenario.Mutant.ReceiveNoise(CreateNoise(
                source,
                NoiseKind.Gunshot,
                1,
                new Vector2(55.0f, 8.0f),
                1.0f));
            scenario.Mutant._PhysicsProcess(0.01);

            TestAssert.Equal(MutantState.Investigate, scenario.Mutant.State,
                "A new gunshot could not start investigation after chase memory expired.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("patrolling-mutant-investigates-gunshot", async () =>
        {
            var scenario = await CreateScenario(context, withPatrol: true);
            Node2D source = AddNoiseSource(scenario.Root, "PatrolGunshot");
            TestAssert.Equal(MutantState.Patrol, scenario.Mutant.State,
                "Test mutant did not start in Patrol.");

            scenario.Mutant.ReceiveNoise(CreateNoise(
                source,
                NoiseKind.Gunshot,
                1,
                new Vector2(60.0f, 0.0f),
                1.0f));
            scenario.Mutant._PhysicsProcess(0.01);

            TestAssert.Equal(MutantState.Investigate, scenario.Mutant.State,
                "Patrol no longer responds to relevant gunshots.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("searching-mutant-accepts-more-relevant-gunshot", async () =>
        {
            var scenario = await CreateScenario(context);
            AcquireChase(scenario.Mutant, scenario.Target);
            ExpireChaseGrace(scenario.Mutant, scenario.Target);
            TestAssert.Equal(MutantState.ChaseLastKnownPosition, scenario.Mutant.State,
                "Test mutant did not enter last-known-position search.");
            Node2D source = AddNoiseSource(scenario.Root, "SearchStimulus");

            scenario.Mutant.ReceiveNoise(CreateNoise(
                source,
                NoiseKind.Footstep,
                1,
                new Vector2(20.0f, 0.0f),
                1.0f));
            scenario.Mutant.ReceiveNoise(CreateNoise(
                source,
                NoiseKind.Gunshot,
                2,
                new Vector2(85.0f, 0.0f),
                1.0f));
            scenario.Mutant._PhysicsProcess(0.01);

            TestAssert.Equal(MutantState.Investigate, scenario.Mutant.State,
                "Search did not accept a more relevant gunshot.");
            TestAssert.True(
                scenario.Mutant.InvestigationPosition.IsEqualApprox(
                    new Vector2(85.0f, 0.0f)),
                "Search selected a lower-priority stimulus.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("direct-sight-overrides-investigate-immediately", async () =>
        {
            var scenario = await CreateScenario(context, withPatrol: true);
            Node2D source = AddNoiseSource(scenario.Root, "InvestigationSource");
            scenario.Mutant.ReceiveNoise(CreateNoise(
                source,
                NoiseKind.Gunshot,
                1,
                new Vector2(65.0f, 0.0f),
                1.0f));
            scenario.Mutant._PhysicsProcess(0.01);
            TestAssert.Equal(MutantState.Investigate, scenario.Mutant.State,
                "Test mutant did not enter Investigate.");

            scenario.Target.Position = new Vector2(-100.0f, 0.0f);
            scenario.Mutant._PhysicsProcess(0.2);

            TestAssert.Equal(MutantState.Chase, scenario.Mutant.State,
                "Direct visual detection did not override Investigate.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("dead-mutant-ignores-new-noise", async () =>
        {
            var scenario = await CreateScenario(context);
            scenario.Mutant.Health.ApplyDamage(new DamageInfo(75));
            TestAssert.Equal(MutantState.Dead, scenario.Mutant.State,
                "Test mutant did not enter Dead.");
            Node2D source = AddNoiseSource(scenario.Root, "PostMortemNoise");
            int stateChanges = 0;
            scenario.Mutant.StateChanged += (_, _) => stateChanges++;

            scenario.Mutant.ReceiveNoise(CreateNoise(
                source,
                NoiseKind.Gunshot,
                1,
                new Vector2(20.0f, 0.0f),
                1.0f));
            scenario.Mutant._PhysicsProcess(0.2);

            TestAssert.Equal(MutantState.Dead, scenario.Mutant.State,
                "A dead mutant reacted to noise.");
            TestAssert.Equal(0, stateChanges,
                "A dead mutant published a new state transition.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("dead-player-does-not-trigger-new-stimulus-state", async () =>
        {
            var scenario = await CreateScenario(context, withPatrol: true);
            scenario.Target.Health.ApplyDamage(new DamageInfo(100));
            TestAssert.Equal(MutantState.Idle, scenario.Mutant.State,
                "Target death did not terminate active targeting.");
            int stateChanges = 0;
            scenario.Mutant.StateChanged += (_, _) => stateChanges++;

            scenario.Mutant.ReceiveNoise(CreateNoise(
                scenario.Target,
                NoiseKind.Gunshot,
                1,
                scenario.Target.GlobalPosition,
                1.0f));
            scenario.Mutant._PhysicsProcess(0.2);

            TestAssert.Equal(MutantState.Idle, scenario.Mutant.State,
                "Noise from a dead player started a new AI state.");
            TestAssert.Equal(0, stateChanges,
                "A dead player produced a new state transition.");
            await context.DisposeNodeAsync(scenario.Root);
        });

        await context.RunAsync("environmental-damage-does-not-reveal-player", async () =>
        {
            var scenario = await CreateScenario(context, withPatrol: true);
            Node2D environmentalSource = AddNoiseSource(
                scenario.Root,
                "EnvironmentalDamage");

            scenario.Mutant.Health.ApplyDamage(new DamageInfo(
                1,
                environmentalSource,
                "Environmental"));

            TestAssert.Equal(MutantState.Patrol, scenario.Mutant.State,
                "Environmental damage incorrectly revealed the player.");
            TestAssert.False(scenario.Mutant.HasLastKnownTargetPosition,
                "Environmental damage created player target memory.");
            await context.DisposeNodeAsync(scenario.Root);
        });

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
            var scenario = await CreateScenario(context);
            scenario.Target.Position = new Vector2(-100.0f, 0.0f);

            scenario.Mutant._PhysicsProcess(0.01);

            TestAssert.True(scenario.Mutant.CanCurrentlySeeTarget,
                "Mutant did not acquire direct sight.");
            TestAssert.True(
                scenario.Mutant.State is MutantState.Chase or MutantState.Attack,
                "Direct sight did not override the previous state.");

            scenario.Target.Health.ApplyDamage(new DamageInfo(100));
            scenario.Mutant._PhysicsProcess(0.2);
            TestAssert.False(scenario.Mutant.CanCurrentlySeeTarget,
                "Mutant continued seeing a dead player.");
            await context.DisposeNodeAsync(scenario.Root);
        });
    }

    private static async Task<(
        Node2D Root,
        MutantController2D Mutant,
        TestPerceptionTarget2D Target)> CreateScenario(
        FeatureTestContext context,
        bool withPatrol = false)
    {
        Node2D root = context.AddNode(new Node2D { Name = "MutantScenarioRoot" });
        MutantController2D mutant = InstantiateMutant(root, withPatrol);
        TestPerceptionTarget2D target = new()
        {
            Name = "Target",
            Position = new Vector2(100.0f, 0.0f),
        };
        root.AddChild(target);
        await context.WaitPhysicsFramesAsync(2);
        mutant.BindTarget(target, target, target);
        return (root, mutant, target);
    }

    private static void AcquireChase(
        MutantController2D mutant,
        TestPerceptionTarget2D target)
    {
        target.Position = new Vector2(-100.0f, 0.0f);
        mutant._PhysicsProcess(0.2);
        TestAssert.Equal(MutantState.Chase, mutant.State,
            "Test setup failed to establish Chase.");
    }

    private static void HideTarget(
        MutantController2D mutant,
        TestPerceptionTarget2D target)
    {
        target.Position = new Vector2(100.0f, 0.0f);
        mutant._PhysicsProcess(0.13);
        TestAssert.False(mutant.CanCurrentlySeeTarget,
            "Test setup failed to hide the target.");
        TestAssert.Equal(MutantState.Chase, mutant.State,
            "Target loss bypassed the chase grace period.");
    }

    private static void ExpireChaseGrace(
        MutantController2D mutant,
        TestPerceptionTarget2D target)
    {
        HideTarget(mutant, target);
        mutant._PhysicsProcess(mutant.Definition!.LostTargetGraceSeconds + 0.01);
        TestAssert.Equal(MutantState.ChaseLastKnownPosition, mutant.State,
            "Test setup failed to expire chase grace into search.");
    }

    private static Node2D AddNoiseSource(Node parent, string name)
    {
        Node2D source = new() { Name = name };
        parent.AddChild(source);
        return source;
    }

    private static PerceivedNoise2D CreateNoise(
        Node source,
        NoiseKind kind,
        ulong sequenceId,
        Vector2 worldPosition,
        float intensity)
    {
        NoiseEvent noise = new(
            source,
            kind,
            intensity,
            sequenceId,
            sequenceId * 0.01,
            $"Test {kind}");
        NoiseOccurrence2D occurrence = new(noise, worldPosition);
        return new PerceivedNoise2D(
            occurrence,
            intensity,
            10.0f,
            1000.0f,
            wasOccluded: false);
    }

    private static async Task<double> SimulateBriefCrossing(
        FeatureTestContext context,
        double delta)
    {
        var scenario = await CreateScenario(context);
        double detectionTime = double.PositiveInfinity;
        double elapsed = 0.0;
        while (elapsed < 1.0)
        {
            scenario.Target.Position = elapsed >= 0.35 && elapsed < 0.55
                ? new Vector2(-100.0f, 0.0f)
                : new Vector2(100.0f, 0.0f);
            scenario.Mutant._PhysicsProcess(delta);
            if (scenario.Mutant.CanCurrentlySeeTarget &&
                double.IsPositiveInfinity(detectionTime))
            {
                detectionTime = elapsed;
            }

            elapsed += delta;
        }

        await context.DisposeNodeAsync(scenario.Root);
        return detectionTime;
    }

    private static MutantController2D InstantiateMutant(
        Node parent,
        bool withPatrol)
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://scenes/enemies/TunnelMutant2D.tscn")
            ?? throw new InvalidOperationException("Could not load mutant scene.");
        MutantController2D mutant = scene.Instantiate<MutantController2D>();
        mutant.Name = "Mutant";
        mutant.SetPhysicsProcess(false);
        if (withPatrol)
        {
            mutant.PatrolPointOffsets = new[]
            {
                Vector2.Zero,
                new Vector2(-40.0f, 0.0f),
            };
        }

        parent.AddChild(mutant);
        return mutant;
    }
}
