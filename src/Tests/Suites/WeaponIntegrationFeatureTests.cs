using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Combat;
using LineZero.Tests.Framework;
using LineZero.World2D;
using LineZero.World2D.Combat;
using LineZero.World2D.Noise;
using LineZero.World2D.Presentation;

namespace LineZero.Tests.Suites;

public sealed class WeaponIntegrationFeatureTests : IFeatureTestSuite
{
    public string Id => "weapon-integration";

    public string Description => "Muzzle obstruction, first-hit damage, weapon FX, ammo, and gunshot noise";

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
            WeaponFxController2D weaponFx = player.GetNode<WeaponFxController2D>("%WeaponFxController2D");
            PointLight2D muzzleLight = player.GetNode<PointLight2D>("%MuzzleFlashLight");
            Sprite2D weaponSprite = player.GetNode<Sprite2D>("%WeaponSprite");
            PlayerCameraZoom2D camera = player.GetNode<PlayerCameraZoom2D>("%Camera2D");
            Vector2 authoredWeaponOffset = weaponSprite.Offset;
            TestAssert.False(weaponFx.HasActiveShotVisuals,
                "Blocked shot displayed weapon FX.");
            TestAssert.False(muzzleLight.Enabled,
                "Blocked shot enabled the muzzle-flash light.");
            TestAssert.Equal(0.0f, weaponFx.Heat01,
                "Blocked shot heated the visual FX state.");
            TestAssert.False(player.IsFiringMovementPenaltyActive,
                "Blocked shot activated the firing movement penalty.");
            TestAssert.False(camera.HasActiveShotShake,
                "Blocked shot activated camera shake.");
            TestAssert.NearlyEqual(authoredWeaponOffset.X, weaponSprite.Offset.X, 1e-6,
                "Blocked shot moved the weapon sprite recoil offset.");
            TestAssert.NearlyEqual(authoredWeaponOffset.Y, weaponSprite.Offset.Y, 1e-6,
                "Blocked shot changed the weapon sprite vertical offset.");

            wall.QueueFree();
            await context.WaitPhysicsFramesAsync(2);
            Marker2D muzzlePoint = player.GetNode<Marker2D>("%MuzzlePoint");
            Vector2 physicalMuzzleBeforeShot = muzzlePoint.GlobalPosition;
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
            TestAssert.True(weaponFx.HasActiveShotVisuals,
                "Valid shot did not display weapon FX.");
            TestAssert.True(muzzleLight.Enabled,
                "Valid shot did not trigger the muzzle-flash light.");
            TestAssert.True(weaponFx.Heat01 > 0.0f,
                "Valid shot did not update the sustained-fire FX heat state.");
            TestAssert.True(player.IsFiringMovementPenaltyActive,
                "Valid shot did not activate the firing movement penalty.");
            TestAssert.True(camera.HasActiveShotShake,
                "Valid shot did not activate camera shake.");
            TestAssert.True(weaponSprite.Offset.X < authoredWeaponOffset.X,
                "Valid shot did not move the weapon sprite backward.");
            TestAssert.NearlyEqual(physicalMuzzleBeforeShot.X, muzzlePoint.GlobalPosition.X, 1e-6,
                "Visual recoil changed the physical muzzle X position.");
            TestAssert.NearlyEqual(physicalMuzzleBeforeShot.Y, muzzlePoint.GlobalPosition.Y, 1e-6,
                "Visual recoil changed the physical muzzle Y position.");

            await context.WaitSecondsAsync(0.4);
            TestAssert.False(player.IsFiringMovementPenaltyActive,
                "Firing movement penalty did not expire after the shot.");
            TestAssert.False(camera.HasActiveShotShake,
                "Camera shake did not settle after the shot.");
            TestAssert.NearlyEqual(authoredWeaponOffset.X, weaponSprite.Offset.X, 1e-4,
                "Weapon recoil did not recover to its authored offset.");
            TestAssert.NearlyEqual(authoredWeaponOffset.Y, weaponSprite.Offset.Y, 1e-6,
                "Weapon recoil introduced a vertical offset.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("aim-input-toggles-aiming-and-combat-disable-clears-it", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "AimInputTestRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);

            PlayerWeaponController2D weapon = player.GetNode<PlayerWeaponController2D>(
                "%PlayerWeaponController2D");
            weapon.SetCombatInputEnabled(true);
            await context.WaitProcessFramesAsync(1);

            int aimingChangedCount = 0;
            weapon.AimingChanged += _ => aimingChangedCount++;
            weapon._UnhandledInput(new InputEventAction
            {
                Action = "aim",
                Pressed = true,
                Strength = 1.0f,
            });
            TestAssert.True(weapon.IsAiming,
                "Pressing aim did not enter aiming state.");

            weapon.SetCombatInputEnabled(false);
            TestAssert.False(weapon.IsAiming,
                "Disabling combat input left aiming active.");
            TestAssert.Equal(2, aimingChangedCount,
                "Aiming state did not publish exactly one enter and one exit notification.");

            await context.DisposeNodeAsync(root);
        });

        await context.RunAsync("automatic-fire-continues-while-trigger-held-and-stops-on-release", async () =>
        {
            Node2D root = context.AddNode(new Node2D { Name = "AutomaticFireTestRoot" });
            NoiseSystem2D noiseSystem = new() { Name = "NoiseSystem" };
            PlayerController2D player = LoadPlayer();
            root.AddChild(noiseSystem);
            root.AddChild(player);
            player.BindNoiseSystem(noiseSystem);
            player.SetPhysicsProcess(false);

            Node2D aimPivot = player.GetNode<Node2D>("%AimPivot");
            aimPivot.GlobalRotation = 0.0f;
            PlayerWeaponController2D weapon = player.GetNode<PlayerWeaponController2D>(
                "%PlayerWeaponController2D");
            weapon.SetCombatInputEnabled(true);
            await context.WaitProcessFramesAsync(1);

            int ammoBefore = weapon.State.CurrentMagazineAmmo;
            weapon._UnhandledInput(new InputEventAction
            {
                Action = "fire",
                Pressed = true,
                Strength = 1.0f,
            });

            TestAssert.Equal(ammoBefore, weapon.State.CurrentMagazineAmmo,
                "Initial trigger press fired outside the physics tick.");
            await context.WaitPhysicsFramesAsync(1);
            TestAssert.Equal(ammoBefore - 1, weapon.State.CurrentMagazineAmmo,
                "Initial trigger press was not consumed on the next physics tick.");

            await context.WaitSecondsAsync(0.24);
            int ammoWhileHeld = weapon.State.CurrentMagazineAmmo;
            TestAssert.True(ammoWhileHeld <= ammoBefore - 2,
                "Holding fire on the AK did not produce repeated shots.");

            weapon._UnhandledInput(new InputEventAction
            {
                Action = "fire",
                Pressed = false,
                Strength = 0.0f,
            });
            int ammoAtRelease = weapon.State.CurrentMagazineAmmo;
            await context.WaitSecondsAsync(0.16);
            TestAssert.Equal(ammoAtRelease, weapon.State.CurrentMagazineAmmo,
                "Automatic fire continued after the trigger was released.");

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
