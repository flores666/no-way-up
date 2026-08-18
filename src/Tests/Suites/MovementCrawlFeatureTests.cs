using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Movement;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Noise;

namespace LineZero.Tests.Suites;

public sealed class MovementCrawlFeatureTests : IFeatureTestSuite
{
    public string Id => "movement-crawl";

    public string Description => "Walk/Sprint-only movement controls and tuning";

    public async Task RunAsync(FeatureTestContext context)
    {
        context.Run("posture-input-actions-are-removed", () =>
        {
            TestAssert.False(InputMap.HasAction("crouch"),
                "Crouch input action still exists after posture removal.");
            TestAssert.False(InputMap.HasAction("crawl"),
                "Crawl input action still exists after posture removal.");
        });

        await context.RunAsync("player-has-one-movement-collider-and-no-posture-state", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "MovementTestRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            await context.WaitProcessFramesAsync(2);

            CollisionShape2D normal = player.GetNode<CollisionShape2D>(
                "%NormalCollisionShape");
            TestAssert.False(normal.Disabled, "Player movement collider is disabled.");
            TestAssert.True(player.GetNodeOrNull<CollisionShape2D>("CrawlCollisionShape") is null,
                "Legacy crawl collider still exists in the player scene.");
            TestAssert.Equal(MovementMode.Walk, player.CurrentMovementMode,
                "Player did not start in Walk mode.");

            await context.DisposeNodeAsync(root);
        });

        context.Run("movement-speeds-match-current-balance", () =>
        {
            PlayerController2D player = LoadPlayer();
            TestAssert.NearlyEqual(198.0, player.MovementSettings!.WalkSpeed, 1e-6,
                "Default walk speed is not reduced by 10 percent.");
            TestAssert.NearlyEqual(272.8, player.MovementSettings.SprintSpeed, 1e-6,
                "Default sprint speed is not reduced by 20 percent.");
            player.Free();
        });
    }

    private static PlayerController2D LoadPlayer()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>("res://scenes/player/Player.tscn")
            ?? throw new System.InvalidOperationException("Could not load player scene.");
        return scene.Instantiate<PlayerController2D>();
    }
}
