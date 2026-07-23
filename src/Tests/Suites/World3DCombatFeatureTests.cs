using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Items;
using LineZero.Gameplay.Noise;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;
using LineZero.World3D;
using LineZero.World3D.Combat;
using LineZero.World3D.Noise;

namespace LineZero.Tests.Suites;

public sealed class World3DCombatFeatureTests : IFeatureTestSuite
{
    public string Id => "world-3d-combat";

    public string Description =>
        "Transactional firearm damage, muzzle safety, first-hit walls, reload, and terminal input";

    public async Task RunAsync(FeatureTestContext context)
    {
        context.Run("firearm-and-damage-publish-only-after-atomic-commit", () =>
        {
            FirearmState firearm = new(
                TestDataFactory.CreateFirearmDefinition(),
                initialMagazineAmmo: 3);
            HealthModel target = new(100);
            FirearmDischargeService service = new();
            List<string> observations = new();
            int healthyFirearmEvents = 0;
            int healthyHealthEvents = 0;
            firearm.Changed += () => throw new InvalidOperationException("Expected.");
            firearm.Changed += () =>
            {
                healthyFirearmEvents++;
                observations.Add($"weapon:{firearm.CurrentMagazineAmmo}:{target.CurrentHealth}");
            };
            target.Changed += _ => throw new InvalidOperationException("Expected.");
            target.Changed += _ =>
            {
                healthyHealthEvents++;
                observations.Add($"health:{firearm.CurrentMagazineAmmo}:{target.CurrentHealth}");
            };

            FirearmDischargeResult result = service.TryDischarge(
                firearm,
                target,
                new DamageInfo(25));

            TestAssert.True(result.Shot.Success && result.DamageApplied,
                "Prepared firearm/damage transaction did not commit.");
            TestAssert.Equal(2, firearm.CurrentMagazineAmmo,
                "Atomic discharge did not consume exactly one round.");
            TestAssert.Equal(75, target.CurrentHealth,
                "Atomic discharge did not apply damage exactly once.");
            TestAssert.Equal(1, healthyFirearmEvents,
                "Throwing firearm subscriber blocked healthy delivery.");
            TestAssert.Equal(1, healthyHealthEvents,
                "Throwing health subscriber blocked healthy delivery.");
            TestAssert.Equal("weapon:2:75", observations[0],
                "Firearm observer saw intermediate target health.");
            TestAssert.Equal("health:2:75", observations[1],
                "Health observer saw intermediate firearm ammunition.");
        });

        await context.RunAsync(
            "muzzle-clearance-and-first-world-hit-are-authoritative",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(8);
                PlayerAimController3D aim =
                    main.Player.GetNode<PlayerAimController3D>(
                        "%PlayerAimController3D");
                PlayerWeaponController3D weapon = main.Weapon;
                NoiseSystem3D noiseSystem =
                    main.GetNode<NoiseSystem3D>("%NoiseSystem3D");
                aim.SetProcess(false);
                Vector3 forwardAim =
                    main.Player.GlobalPosition + (Vector3.Forward * 20.0f);
                forwardAim.Y = 0.0f;
                TestAssert.True(aim.TryApplyWorldAimPoint(forwardAim),
                    "Combat fixture could not establish a valid world aim point.");
                await context.WaitPhysicsFramesAsync();

                Marker3D weaponOrigin = main.PlayerVisual.WeaponOrigin;
                Marker3D muzzle = main.PlayerVisual.MuzzleSocket;
                StaticBody3D muzzleWall = CreateWall(
                    "ThinMuzzleWall3D",
                    weaponOrigin.GlobalPosition.Lerp(
                        muzzle.GlobalPosition,
                        0.55f),
                    new Vector3(2.0f, 2.0f, 0.06f));
                main.AddChild(muzzleWall);
                await context.WaitPhysicsFramesAsync(2);
                int gunshotNoiseCount = 0;
                noiseSystem.NoiseEmitted += occurrence =>
                {
                    if (occurrence.Noise.Kind == NoiseKind.Gunshot)
                    {
                        gunshotNoiseCount++;
                    }
                };
                int ammoBefore = weapon.State.CurrentMagazineAmmo;

                FirearmDischargeResult blockedAtMuzzle = weapon.TryFire();

                TestAssert.Equal(
                    FirearmShotStatus.MuzzleObstructed,
                    blockedAtMuzzle.Shot.Status,
                    "A thin wall across the authored muzzle segment was bypassed.");
                TestAssert.Equal(ammoBefore, weapon.State.CurrentMagazineAmmo,
                    "Muzzle-obstructed shot consumed ammunition.");
                TestAssert.Equal(0, gunshotNoiseCount,
                    "Muzzle-obstructed shot emitted gameplay noise.");

                muzzleWall.QueueFree();
                TestDamageableTarget3D target = CreateTarget(
                    main.Player.GlobalPosition + (Vector3.Forward * 6.0f));
                main.AddChild(target);
                await context.WaitPhysicsFramesAsync(2);
                forwardAim = target.GlobalPosition;
                forwardAim.Y = 0.0f;
                TestAssert.True(aim.TryApplyWorldAimPoint(forwardAim),
                    "Combat fixture lost its target aim point.");

                FirearmDischargeResult clearShot = weapon.TryFire();

                TestAssert.True(clearShot.Shot.Success && clearShot.DamageApplied,
                    "A clear muzzle ray did not hit the first firearm target.");
                TestAssert.Equal(75, target.Health.CurrentHealth,
                    "One 3D shot did not apply exactly one damage mutation.");
                TestAssert.Equal(ammoBefore - 1, weapon.State.CurrentMagazineAmmo,
                    "One valid 3D shot did not consume exactly one round.");
                TestAssert.Equal(1, gunshotNoiseCount,
                    "One valid 3D shot did not emit exactly one gunshot occurrence.");
                FirearmShotOccurrence3D occurrence = weapon.LastShotOccurrence
                    ?? throw new TestAssertionException(
                        "Valid 3D shot did not expose its completed occurrence.");
                TestAssert.NearlyEqual(
                    0.0,
                    occurrence.MuzzleOrigin.DistanceTo(muzzle.GlobalPosition),
                    0.00001,
                    "Physical 3D shot did not begin at the muzzle marker.");
                TestAssert.True(
                    occurrence.SafeNoiseOrigin.DistanceTo(main.Player.GlobalPosition) < 1.5f &&
                    occurrence.SafeNoiseOrigin.DistanceTo(occurrence.MuzzleOrigin) > 0.2f,
                    "Gunshot noise did not originate from the safe player side.");

                await context.WaitPhysicsFramesAsync(20);
                StaticBody3D lineWall = CreateWall(
                    "FirstHitWall3D",
                    muzzle.GlobalPosition.Lerp(target.GlobalPosition + Vector3.Up, 0.5f),
                    new Vector3(3.0f, 2.5f, 0.08f));
                main.AddChild(lineWall);
                await context.WaitPhysicsFramesAsync(2);
                int targetHealthBeforeBlockedShot = target.Health.CurrentHealth;
                int ammoBeforeBlockedShot = weapon.State.CurrentMagazineAmmo;

                FirearmDischargeResult wallHit = weapon.TryFire();

                TestAssert.True(wallHit.Shot.Success && !wallHit.DamageApplied,
                    "A wall hit should fire but must not mutate target health.");
                TestAssert.Equal(
                    targetHealthBeforeBlockedShot,
                    target.Health.CurrentHealth,
                    "Muzzle ray damaged a target through the first solid wall.");
                TestAssert.Equal(
                    ammoBeforeBlockedShot - 1,
                    weapon.State.CurrentMagazineAmmo,
                    "A valid wall impact did not conserve the fired round.");
            });

        await context.RunAsync(
            "reload-cancellation-and-terminal-state-conserve-ammunition",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(4);
                PlayerWeaponController3D weapon = main.Weapon;
                ItemDefinition ammo = ResourceLoader.Load<ItemDefinition>(
                    "res://data/items/PistolAmmo.tres")
                    ?? throw new TestAssertionException(
                        "Could not load pistol ammunition definition.");
                main.Player.Inventory.TryAdd(ammo, 5);
                int totalBefore = TotalAmmo(weapon, main, ammo.Id);

                ReloadResult started = weapon.TryBeginReload();
                ReloadResult canceled = weapon.CancelReload();
                TestAssert.Equal(ReloadStatus.Started, started.Status,
                    "3D adapter did not start a valid reload.");
                TestAssert.Equal(ReloadStatus.Canceled, canceled.Status,
                    "3D adapter did not cancel an active reload.");
                TestAssert.Equal(totalBefore, TotalAmmo(weapon, main, ammo.Id),
                    "Canceling a 3D reload changed total ammunition.");

                TestAssert.Equal(
                    ReloadStatus.Started,
                    weapon.TryBeginReload().Status,
                    "3D adapter could not restart a canceled reload.");
                Timer reloadTimer = weapon.GetNode<Timer>("%ReloadTimer3D");
                reloadTimer.EmitSignal(Timer.SignalName.Timeout);
                TestAssert.False(weapon.State.IsReloading,
                    "Reload timeout did not complete the shared reload transaction.");
                TestAssert.Equal(totalBefore, TotalAmmo(weapon, main, ammo.Id),
                    "Completed 3D reload duplicated or lost ammunition.");

                int magazineBeforeTerminalInput = weapon.State.CurrentMagazineAmmo;
                main.SetPrototypeCompleted(true);
                FirearmDischargeResult terminalShot = weapon.TryFire();
                TestAssert.Equal(
                    FirearmShotStatus.CombatDisabled,
                    terminalShot.Shot.Status,
                    "Prototype completion did not disable 3D combat.");
                TestAssert.Equal(
                    magazineBeforeTerminalInput,
                    weapon.State.CurrentMagazineAmmo,
                    "Terminal combat input consumed ammunition.");
            });
    }

    private static TestDamageableTarget3D CreateTarget(Vector3 position)
    {
        TestDamageableTarget3D target = new()
        {
            Name = "DamageableTarget3D",
            Position = position,
            CollisionLayer = CollisionLayers3D.FirearmTarget,
            CollisionMask = 0
        };
        target.AddChild(new CollisionShape3D
        {
            Position = Vector3.Up,
            Shape = new BoxShape3D
            {
                Size = new Vector3(1.0f, 2.0f, 1.0f)
            }
        });
        return target;
    }

    private static StaticBody3D CreateWall(
        string name,
        Vector3 position,
        Vector3 size)
    {
        StaticBody3D wall = new()
        {
            Name = name,
            Position = position,
            CollisionLayer = CollisionLayers3D.World,
            CollisionMask = 0
        };
        wall.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = size }
        });
        return wall;
    }

    private static int TotalAmmo(
        PlayerWeaponController3D weapon,
        Main3D main,
        string ammoItemId)
    {
        return weapon.State.CurrentMagazineAmmo +
               main.Player.Inventory.CountByItemId(ammoItemId);
    }
}
