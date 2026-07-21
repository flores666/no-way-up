using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Combat;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Combat;
using LineZero.World2D.Noise;

namespace LineZero.Tests.Suites;

public sealed class WeaponIntegrationFeatureTests : IFeatureTestSuite
{
    public string Id => "weapon-integration";

    public string Description => "Muzzle obstruction, first-hit damage, tracer, ammo, and gunshot noise";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("wall-before-muzzle-rejects-shot-before-mutation", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "WeaponTestRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            DamageableTarget2D target = LoadTarget();
            target.Position = new Vector2(80.0f, 0.0f);
            StaticBody2D wall = CreateWall(new Vector2(20.0f, 0.0f));
            root.AddChild(noiseSystem);
            root.AddChild(player);
            root.AddChild(target);
            root.AddChild(wall);
            player.BindNoiseSystem(noiseSystem);
            player.SetProcess(false);
            Node2D aimPivot = player.GetNode<Node2D>("%AimPivot");
            aimPivot.GlobalRotation = 0.0f;
            await context.WaitPhysicsFramesAsync(3);
            AssertWallBlocksMuzzleSegment(player, wall);
            PlayerWeaponController2D weapon = player.GetNode<PlayerWeaponController2D>(
                "%PlayerWeaponController2D");
            weapon.SetCombatInputEnabled(true);
            int noiseCount = 0;
            noiseSystem.NoiseEmitted += occurrence =>
            {
                if (occurrence.Noise.Kind == LineZero.Gameplay.Noise.NoiseKind.Gunshot)
                {
                    noiseCount++;
                }
            };
            int ammoBefore = weapon.State.CurrentMagazineAmmo;
            int targetHealthBefore = target.Health.CurrentHealth;

            FirearmShotResult blocked = weapon.TryFire();

            TestAssert.Equal(FirearmShotStatus.MuzzleObstructed, blocked.Status,
                "Wall before muzzle did not reject the shot.");
            TestAssert.Equal(ammoBefore, weapon.State.CurrentMagazineAmmo,
                "Blocked shot consumed ammunition.");
            TestAssert.Equal(targetHealthBefore, target.Health.CurrentHealth,
                "Blocked shot damaged a target behind the wall.");
            TestAssert.Equal(0, noiseCount, "Blocked shot emitted gunshot noise.");
            Line2D tracer = weapon.GetNode<Line2D>("%TracerLine");
            TestAssert.False(tracer.Visible, "Blocked shot displayed a tracer.");

            wall.QueueFree();
            await context.WaitPhysicsFramesAsync(2);
            FirearmShotResult clear = weapon.TryFire();
            TestAssert.Equal(FirearmShotStatus.Fired, clear.Status,
                "Clear shot was rejected after wall removal.");
            TestAssert.Equal(ammoBefore - 1, weapon.State.CurrentMagazineAmmo,
                "Clear shot did not consume exactly one round.");
            TestAssert.Equal(targetHealthBefore - weapon.State.Definition.Damage,
                target.Health.CurrentHealth,
                "Clear hitscan did not damage the first target correctly.");
            TestAssert.Equal(1, noiseCount,
                "One valid shot did not emit exactly one gunshot noise.");

            await context.DisposeNodeAsync(root);
        });
    }

    private static void AssertWallBlocksMuzzleSegment(
        PlayerController2D player,
        StaticBody2D expectedWall)
    {
        Marker2D weaponOrigin = player.GetNode<Marker2D>("%WeaponOrigin");
        Marker2D muzzlePoint = player.GetNode<Marker2D>("%MuzzlePoint");
        PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(
            weaponOrigin.GlobalPosition,
            muzzlePoint.GlobalPosition + Vector2.Right,
            CollisionLayers2D.World);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.HitFromInside = true;
        query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };

        Godot.Collections.Dictionary obstruction =
            player.GetWorld2D().DirectSpaceState.IntersectRay(query);
        TestAssert.True(obstruction.Count > 0,
            "Weapon test fixture did not place a wall across the muzzle segment.");
        GodotObject? collider = obstruction["collider"].AsGodotObject();
        TestAssert.Same(expectedWall, collider!,
            "Weapon test fixture ray hit an unexpected collider.");
    }

    private static PlayerController2D LoadPlayer()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>("res://scenes/player/Player.tscn")
            ?? throw new System.InvalidOperationException("Could not load player scene.");
        return scene.Instantiate<PlayerController2D>();
    }

    private static DamageableTarget2D LoadTarget()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://scenes/combat/DamageableTarget2D.tscn")
            ?? throw new System.InvalidOperationException(
                "Could not load damageable-target scene.");
        return scene.Instantiate<DamageableTarget2D>();
    }

    private static StaticBody2D CreateWall(Vector2 position)
    {
        StaticBody2D wall = new()
        {
            Name = "MuzzleBlockingWall",
            Position = position,
            CollisionLayer = CollisionLayers2D.World,
            CollisionMask = CollisionLayers2D.World,
        };
        wall.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(2.0f, 120.0f) },
        });
        return wall;
    }
}
