using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Gameplay.Enemies;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Noise;
using LineZero.Tests.Framework;
using LineZero.World3D;
using LineZero.World3D.Enemies;
using LineZero.World3D.Flashlight;
using LineZero.World3D.Noise;

namespace LineZero.Tests.Suites;

public sealed class World3DMutantFeatureTests : IFeatureTestSuite
{
    public string Id => "world-3d-mutant";

    public string Description =>
        "3D navigation, perception priority, hearing, chase stability, melee LOS, death, and terminal AI";

    public async Task RunAsync(FeatureTestContext context)
    {
        context.Run("authoritative-decision-priority-is-stable", () =>
        {
            MutantDecisionContext visible = new(
                IsAlive: true,
                IsTerminal: false,
                IsTargetAlive: true,
                CanSeeTarget: true,
                IsTargetInAttackRange: false,
                HasChaseGrace: true,
                HasLastKnownTarget: true,
                HasRelevantNoise: true,
                IsSearching: true,
                HasPatrolRoute: true);
            TestAssert.Equal(MutantState.Chase, MutantDecisionRules.Decide(visible),
                "Confirmed sight did not outrank memory and noise.");

            MutantDecisionContext grace = visible with
            {
                CanSeeTarget = false,
                HasChaseGrace = true
            };
            TestAssert.Equal(MutantState.Chase, MutantDecisionRules.Decide(grace),
                "Chase grace was downgraded by investigation noise.");

            MutantDecisionContext memory = grace with
            {
                HasChaseGrace = false,
                HasLastKnownTarget = true
            };
            TestAssert.Equal(
                MutantState.ChaseLastKnownPosition,
                MutantDecisionRules.Decide(memory),
                "Last-known target memory did not outrank unconfirmed noise.");

            MutantDecisionContext terminal = visible with { IsTerminal = true };
            TestAssert.Equal(MutantState.Dead, MutantDecisionRules.Decide(terminal),
                "Terminal state did not outrank every active AI stimulus.");
        });

        await context.RunAsync(
            "navigation-region-patrol-and-hearing-are-connected",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(8);
                NavigationRegion3D region =
                    main.GetNode<Node3D>("%TestLevel3D")
                        .GetNode<NavigationRegion3D>("%NavigationRegion3D");
                MutantController3D mutant = main.Mutant;
                NavigationAgent3D agent =
                    mutant.GetNode<NavigationAgent3D>("%NavigationAgent3D");
                TestAssert.True(region.NavigationMesh is not null,
                    "Technical 3D level has no authored navigation mesh.");
                TestAssert.True(agent.GetNavigationMap().IsValid,
                    "Mutant NavigationAgent3D has no navigation map.");
                TestAssert.Equal(MutantState.Patrol, mutant.State,
                    "Authored 3D mutant did not begin on its patrol route.");

                PlayerFlashlightController3D flashlight =
                    main.PlayerVisual.FlashlightController;
                flashlight.TurnOff();
                Vector3 patrolStart = mutant.GlobalPosition;
                await context.WaitPhysicsFramesAsync(70);
                TestAssert.True(
                    mutant.GlobalPosition.DistanceTo(patrolStart) > 0.2f,
                    "NavigationAgent3D patrol produced no physical movement.");

                Node3D source = new() { Name = "UnconfirmedNoise3D" };
                main.AddChild(source);
                NoiseSystem3D noiseSystem =
                    main.GetNode<NoiseSystem3D>("%NoiseSystem3D");
                noiseSystem.EmitNoise(
                    source,
                    NoiseKind.Gunshot,
                    1.0f,
                    mutant.GlobalPosition + (Vector3.Right * 2.0f),
                    description: "Patrol investigation fixture");
                await context.WaitPhysicsFramesAsync(2);
                TestAssert.Equal(MutantState.Investigate, mutant.State,
                    "Patrolling 3D mutant did not investigate a relevant gunshot.");
                TestAssert.True(mutant.HasInvestigationTarget,
                    "Accepted 3D noise did not retain an investigation position.");
            });

        await context.RunAsync(
            "sight-and-player-damage-preserve-chase-over-noise",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(6);
                MutantController3D mutant = main.Mutant;
                PlayerFlashlightController3D flashlight =
                    main.PlayerVisual.FlashlightController;
                flashlight.TurnOff();
                main.Player.GlobalPosition =
                    mutant.GlobalPosition + (Vector3.Left * 6.0f);
                main.Player.Velocity = Vector3.Zero;
                await context.WaitPhysicsFramesAsync(16);
                TestAssert.True(mutant.CanCurrentlySeeTarget,
                    "3D mutant did not confirm a visible target in its FOV.");
                TestAssert.Equal(MutantState.Chase, mutant.State,
                    "Confirmed 3D sight did not enter Chase.");

                List<MutantState> enteredStates = new();
                mutant.StateChanged += (_, next) => enteredStates.Add(next);
                main.Player.GlobalPosition =
                    mutant.GlobalPosition + (Vector3.Right * 20.0f);
                main.Player.Velocity = Vector3.Zero;
                await context.WaitPhysicsFramesAsync(4);
                Node3D source = new() { Name = "LowerPriorityGunshot3D" };
                main.AddChild(source);
                main.GetNode<NoiseSystem3D>("%NoiseSystem3D").EmitNoise(
                    source,
                    NoiseKind.Gunshot,
                    2.0f,
                    mutant.GlobalPosition + Vector3.Forward,
                    description: "Competing gunshot");
                mutant.Health.ApplyDamage(new DamageInfo(
                    1,
                    main.Player,
                    "Player test shot"));
                await context.WaitPhysicsFramesAsync(2);

                TestAssert.Equal(MutantState.Chase, mutant.State,
                    "Gunshot or player damage downgraded an active 3D Chase.");
                TestAssert.False(enteredStates.Contains(MutantState.Investigate),
                    "3D Chase passed through a lower-priority Investigate state.");
            });

        await context.RunAsync(
            "melee-requires-clear-line-and-terminal-states-stop-ai",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(6);
                MutantController3D mutant = main.Mutant;
                mutant.GlobalPosition = new Vector3(10.0f, 0.05f, 9.0f);
                mutant.Velocity = Vector3.Zero;
                main.Player.GlobalPosition =
                    mutant.GlobalPosition + (Vector3.Left * 1.0f);
                main.Player.Velocity = Vector3.Zero;
                StaticBody3D wall = CreateMeleeWall(
                    mutant.GlobalPosition + (Vector3.Left * 0.5f));
                main.AddChild(wall);
                await context.WaitPhysicsFramesAsync(4);
                int healthBeforeBlockedMelee = main.Player.Health.CurrentHealth;
                await context.WaitSecondsAsync(0.45);
                TestAssert.Equal(
                    healthBeforeBlockedMelee,
                    main.Player.Health.CurrentHealth,
                    "3D mutant attacked through a solid wall.");

                wall.QueueFree();
                await context.WaitPhysicsFramesAsync(2);
                await context.WaitSecondsAsync(0.55);
                TestAssert.True(
                    main.Player.Health.CurrentHealth < healthBeforeBlockedMelee,
                    "3D mutant did not apply melee after LOS became clear.");

                int healthBeforeTerminal = main.Player.Health.CurrentHealth;
                main.SetPrototypeCompleted(true);
                await context.WaitSecondsAsync(0.35);
                TestAssert.Equal(
                    healthBeforeTerminal,
                    main.Player.Health.CurrentHealth,
                    "Prototype completion did not stop pending mutant attacks.");
                TestAssert.Equal(MutantState.Dead, mutant.State,
                    "Terminal completion did not enter the highest-priority AI state.");
                TestAssert.False(mutant.IsPhysicsProcessing(),
                    "Terminal 3D mutant continued physics processing.");
            });

        await context.RunAsync("dead-mutant-ignores-noise-and-navigation", async () =>
        {
            Main3D main = context.InstantiateScene<Main3D>(
                "res://scenes/3d/Main3D.tscn");
            await context.WaitPhysicsFramesAsync(4);
            MutantController3D mutant = main.Mutant;
            mutant.Health.ApplyDamage(new DamageInfo(mutant.Health.MaxHealth));
            TestAssert.Equal(MutantState.Dead, mutant.State,
                "Lethal 3D damage did not enter Dead.");
            TestAssert.False(mutant.IsPhysicsProcessing(),
                "Dead 3D mutant continued navigation processing.");
            ulong sequenceBefore = mutant.LastProcessedNoiseSequence;
            Node3D source = new() { Name = "PostMortemNoise3D" };
            main.AddChild(source);
            main.GetNode<NoiseSystem3D>("%NoiseSystem3D").EmitNoise(
                source,
                NoiseKind.Gunshot,
                1.0f,
                mutant.GlobalPosition,
                description: "Post-mortem noise");
            await context.WaitPhysicsFramesAsync(2);
            TestAssert.Equal(sequenceBefore, mutant.LastProcessedNoiseSequence,
                "Dead 3D mutant processed new gameplay noise.");
        });

        await context.RunAsync(
            "unreachable-last-known-search-has-a-maximum-duration",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(6);
                MutantController3D mutant = main.Mutant;
                PlayerFlashlightController3D flashlight =
                    main.PlayerVisual.FlashlightController;
                flashlight.TurnOff();
                main.Player.GlobalPosition =
                    mutant.GlobalPosition + (Vector3.Left * 6.0f);
                await context.WaitPhysicsFramesAsync(16);
                TestAssert.Equal(MutantState.Chase, mutant.State,
                    "Search timeout fixture did not first acquire Chase.");

                main.Player.GlobalPosition =
                    mutant.GlobalPosition + (Vector3.Right * 20.0f);
                mutant._PhysicsProcess(
                    mutant.Definition!.PerceptionIntervalSeconds + 0.01);
                mutant._PhysicsProcess(
                    mutant.Definition.LostTargetGraceSeconds + 0.1);
                TestAssert.Equal(MutantState.ChaseLastKnownPosition, mutant.State,
                    "3D mutant did not begin last-known-position search.");
                mutant._PhysicsProcess(
                    mutant.Definition.MaximumSearchSeconds + 0.1);
                TestAssert.Equal(MutantState.Patrol, mutant.State,
                    "Unreachable 3D target memory exceeded MaximumSearchSeconds.");
                TestAssert.False(mutant.HasLastKnownTargetPosition,
                    "Expired 3D search retained authoritative target memory.");
            });
    }

    private static StaticBody3D CreateMeleeWall(Vector3 position)
    {
        StaticBody3D wall = new()
        {
            Name = "MeleeBlockingWall3D",
            Position = position + (Vector3.Up * 1.0f),
            CollisionLayer = CollisionLayers3D.World,
            CollisionMask = 0
        };
        wall.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(0.12f, 2.4f, 2.4f)
            }
        });
        return wall;
    }
}
