using System;
using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Items;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Presentation;
using LineZero.Tests.Framework;
using LineZero.World3D;
using LineZero.World3D.Combat;
using LineZero.World3D.Presentation;

namespace LineZero.Tests.Suites;

public sealed class PlayerPresentation3DFeatureTests : IFeatureTestSuite
{
    private const double Tolerance = 0.001;

    public string Id => "player-presentation-3d";

    public string Description =>
        "Replaceable player visuals, aim-relative locomotion, typed actions, sockets, and fallback safety";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync(
            "physical-collision-and-gameplay-sensors-stay-outside-player-visual",
            async () =>
            {
                PlayerController3D player =
                    context.InstantiateScene<PlayerController3D>(
                        "res://scenes/3d/player/Player3D.tscn");
                await context.WaitProcessFramesAsync(2);

                TestAssert.True(
                    player.GetNode<CollisionShape3D>("%NormalCollisionShape3D")
                        .Shape is CapsuleShape3D,
                    "Player3D lost its explicit physical movement collider.");
                TestAssert.Same(
                    player,
                    player.GetNode<Area3D>("%PlayerInteractionSensor3D").GetParent(),
                    "Interaction sensor moved into the presentation hierarchy.");
                TestAssert.Same(
                    player,
                    player.GetNode<Area3D>("%PlayerHazardSensor3D").GetParent(),
                    "Hazard sensor moved into the presentation hierarchy.");
                TestAssert.False(
                    HasCollisionDescendant(player.Visual),
                    "PlayerVisual3D contains physical or gameplay collision nodes.");
            });

        context.Run("local-locomotion-covers-cardinal-and-diagonal-directions", () =>
        {
            AssertBlend(Vector3.Forward * 5.0f, new Vector2(0.0f, 1.0f),
                "Forward locomotion blend is incorrect.");
            AssertBlend(Vector3.Back * 5.0f, new Vector2(0.0f, -1.0f),
                "Backward locomotion blend is incorrect.");
            AssertBlend(Vector3.Left * 5.0f, new Vector2(-1.0f, 0.0f),
                "Left strafe locomotion blend is incorrect.");
            AssertBlend(Vector3.Right * 5.0f, new Vector2(1.0f, 0.0f),
                "Right strafe locomotion blend is incorrect.");
            AssertBlend(
                (Vector3.Right + Vector3.Forward).Normalized() * 5.0f,
                new Vector2(Mathf.Sqrt(0.5f), Mathf.Sqrt(0.5f)),
                "Diagonal locomotion blend is incorrect.");
        });

        context.Run("idle-threshold-uses-hysteresis-without-zero-speed-oscillation", () =>
        {
            PlayerLocomotionBlendResult idle = PlayerLocomotionBlend3D.Calculate(
                Vector3.Zero,
                Vector3.Forward,
                Vector3.Right,
                5.0f,
                0.05f,
                0.12f,
                wasMoving: false);
            PlayerLocomotionBlendResult belowStart =
                PlayerLocomotionBlend3D.Calculate(
                    Vector3.Forward * 0.08f,
                    Vector3.Forward,
                    Vector3.Right,
                    5.0f,
                    0.05f,
                    0.12f,
                    wasMoving: false);
            PlayerLocomotionBlendResult preserved =
                PlayerLocomotionBlend3D.Calculate(
                    Vector3.Forward * 0.08f,
                    Vector3.Forward,
                    Vector3.Right,
                    5.0f,
                    0.05f,
                    0.12f,
                    wasMoving: true);
            PlayerLocomotionBlendResult stopped =
                PlayerLocomotionBlend3D.Calculate(
                    Vector3.Forward * 0.04f,
                    Vector3.Forward,
                    Vector3.Right,
                    5.0f,
                    0.05f,
                    0.12f,
                    wasMoving: true);

            TestAssert.False(idle.IsMoving,
                "Zero velocity did not select Idle.");
            TestAssert.False(belowStart.IsMoving,
                "Idle exited below the configured start threshold.");
            TestAssert.True(preserved.IsMoving,
                "Moving state did not preserve hysteresis above the stop threshold.");
            TestAssert.False(stopped.IsMoving,
                "Moving state did not enter Idle below the stop threshold.");
        });

        context.Run("movement-modes-select-authoritative-presentation-profiles", () =>
        {
            PlayerPresentationStateMachine machine = CreateStateMachine();
            machine.UpdateLocomotion(MovementMode.Walk, MovementMode.Walk, false);
            TestAssert.Equal(PlayerPresentationState.Idle, machine.CurrentState,
                "Standing zero velocity did not select Idle.");
            machine.UpdateLocomotion(MovementMode.Walk, MovementMode.Walk, true);
            TestAssert.Equal(PlayerPresentationState.Walk, machine.CurrentState,
                "Walk did not select standing locomotion.");
            machine.UpdateLocomotion(MovementMode.Sprint, MovementMode.Walk, true);
            TestAssert.Equal(PlayerPresentationState.Sprint, machine.CurrentState,
                "Authoritative Sprint mode did not select Sprint presentation.");
            machine.UpdateLocomotion(MovementMode.Crouch, MovementMode.Crouch, false);
            TestAssert.Equal(PlayerPresentationState.CrouchIdle, machine.CurrentState,
                "Crouch idle profile is incorrect.");
            machine.UpdateLocomotion(MovementMode.Crouch, MovementMode.Crouch, true);
            TestAssert.Equal(PlayerPresentationState.CrouchWalk, machine.CurrentState,
                "Crouch movement profile is incorrect.");
            machine.UpdateLocomotion(MovementMode.Crawl, MovementMode.Crawl, false);
            TestAssert.Equal(PlayerPresentationState.CrawlIdle, machine.CurrentState,
                "Crawl idle profile is incorrect.");
            machine.UpdateLocomotion(MovementMode.Crawl, MovementMode.Crawl, true);
            TestAssert.Equal(PlayerPresentationState.CrawlMove, machine.CurrentState,
                "Crawl movement profile is incorrect.");
        });

        await context.RunAsync(
            "visual-yaw-follows-aim-without-rotating-movement-body-or-camera",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(3);
                PlayerAimController3D aim =
                    main.Player.GetNode<PlayerAimController3D>(
                        "%PlayerAimController3D");
                TopDownCamera3D camera = main.GetNode<TopDownCamera3D>(
                    "%TopDownCamera3D");
                Vector3 bodyRotation = main.Player.GlobalRotation;
                Vector3 cameraRotation = camera.GlobalRotation;
                Basis cameraBasis = camera.GlobalTransform.Basis;
                Vector3 movementBefore = GroundMovement3D.CalculateTargetVelocity(
                    Vector2.Up,
                    -cameraBasis.Z,
                    cameraBasis.X,
                    main.Player.WalkingSpeed);

                aim.SetProcess(false);
                TestAssert.True(
                    aim.TryApplyWorldAimPoint(
                        main.Player.GlobalPosition + (Vector3.Right * 8.0f)),
                    "Presentation fixture could not apply a valid right aim point.");
                await context.WaitSecondsAsync(0.25);
                float pivotYaw = main.Player.GetNode<Node3D>(
                    "%VisualPivot3D").GlobalRotation.Y;

                TestAssert.NearlyEqual(
                    0.0,
                    Mathf.AngleDifference(
                        main.PlayerVisual.CurrentVisualYaw,
                        pivotYaw),
                    0.08,
                    "Smoothed visual yaw did not converge on validated aim yaw.");
                AssertVectorNearlyEqual(bodyRotation, main.Player.GlobalRotation,
                    "Aiming rotated the CharacterBody3D.");
                AssertVectorNearlyEqual(cameraRotation, camera.GlobalRotation,
                    "Aiming rotated the fixed camera.");
                Vector3 movementAfter = GroundMovement3D.CalculateTargetVelocity(
                    Vector2.Up,
                    -cameraBasis.Z,
                    cameraBasis.X,
                    main.Player.WalkingSpeed);
                AssertVectorNearlyEqual(movementBefore, movementAfter,
                    "Aiming changed screen-relative movement direction.");
            });

        await context.RunAsync(
            "blocked-posture-transition-does-not-change-visual-profile",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(4);
                TestAssert.True(
                    main.Player.TrySetPosture(MovementMode.Crawl),
                    "Player could not enter Crawl for blocked visual coverage.");
                StaticBody3D ceiling = CreateCeiling(
                    main.Player.GlobalPosition + new Vector3(0.0f, 1.15f, 0.0f));
                main.AddChild(ceiling);
                await context.WaitPhysicsFramesAsync(2);

                TestAssert.False(
                    main.Player.TrySetPosture(
                        MovementMode.Crouch,
                        notifyOnFailure: false),
                    "Blocked Crawl-to-Crouch transition unexpectedly succeeded.");
                TestAssert.Equal(
                    PlayerPresentationProfile.Crawl,
                    main.PlayerVisual.CurrentProfile,
                    "Rejected posture transition changed the visual profile.");
            });

        await context.RunAsync(
            "completed-shot-starts-one-fire-presentation-and-rejection-does-not",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(5);
                PlayerAimController3D aim =
                    main.Player.GetNode<PlayerAimController3D>(
                        "%PlayerAimController3D");
                aim.SetProcess(false);
                TestAssert.True(
                    aim.TryApplyWorldAimPoint(
                        main.Player.GlobalPosition + (Vector3.Forward * 12.0f)),
                    "Fire presentation fixture could not establish aim.");
                int before = main.PlayerVisual.Presentation.FirePresentationCount;

                FirearmDischargeResult first = main.Weapon.TryFire();
                FirearmDischargeResult immediateRepeat = main.Weapon.TryFire();

                TestAssert.True(first.Shot.Success,
                    "Completed-shot presentation fixture did not fire.");
                TestAssert.False(immediateRepeat.Shot.Success,
                    "Immediate repeat unexpectedly bypassed the firearm interval.");
                TestAssert.Equal(
                    before + 1,
                    main.PlayerVisual.Presentation.FirePresentationCount,
                    "One completed shot did not produce exactly one fire presentation.");
                MeshInstance3D muzzleFlash =
                    main.PlayerVisual.MuzzleSocket.GetNode<MeshInstance3D>(
                        "MuzzleFlash3D");
                TestAssert.True(muzzleFlash.Visible,
                    "Completed shot did not activate the reused muzzle flash.");
                TestAssert.NearlyEqual(
                    0.0,
                    muzzleFlash.GlobalPosition.DistanceTo(
                        main.PlayerVisual.MuzzleSocket.GlobalPosition),
                    Tolerance,
                    "Muzzle flash did not originate at MuzzleSocket.");
            });

        await context.RunAsync(
            "reload-presentation-requires-start-and-cancel-restores-locomotion",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(3);
                ItemDefinition ammo = ResourceLoader.Load<ItemDefinition>(
                    "res://data/items/PistolAmmo.tres")
                    ?? throw new TestAssertionException(
                        "Could not load pistol ammunition definition.");
                main.Player.Inventory.TryAdd(ammo, 5);
                int before = main.PlayerVisual.Presentation.ReloadPresentationCount;

                ReloadResult started = main.Weapon.TryBeginReload();
                ReloadResult duplicate = main.Weapon.TryBeginReload();
                TestAssert.Equal(ReloadStatus.Started, started.Status,
                    "Valid reload did not start.");
                TestAssert.Equal(ReloadStatus.AlreadyReloading, duplicate.Status,
                    "Duplicate reload was not rejected by gameplay state.");
                TestAssert.Equal(
                    before + 1,
                    main.PlayerVisual.Presentation.ReloadPresentationCount,
                    "Rejected reload started an extra presentation.");
                TestAssert.Equal(
                    PlayerPresentationState.Reload,
                    main.PlayerVisual.CurrentState,
                    "Successful reload did not select Reload presentation.");

                TestAssert.Equal(
                    ReloadStatus.Canceled,
                    main.Weapon.CancelReload().Status,
                    "Active reload did not cancel.");
                TestAssert.False(main.PlayerVisual.Presentation.IsReloading,
                    "Reload cancellation left presentation latched.");
                TestAssert.True(
                    main.PlayerVisual.CurrentState is
                        PlayerPresentationState.Idle or
                        PlayerPresentationState.Walk,
                    "Reload cancellation did not return to locomotion.");
            });

        context.Run("hit-is-not-duplicated-and-death-overrides-all-actions", () =>
        {
            PlayerPresentationStateMachine machine = CreateStateMachine();
            TestAssert.True(machine.ObserveCompletedDamage(
                    changed: true,
                    causedDeath: false),
                "Completed non-lethal damage did not start HitReaction.");
            TestAssert.Equal(1, machine.HitPresentationCount,
                "One damage event did not produce one hit presentation.");
            TestAssert.Equal(PlayerPresentationState.HitReaction, machine.CurrentState,
                "Hit reaction did not outrank locomotion.");
            machine.ObserveReload(ReloadStatus.Started);
            machine.ObserveDeath();
            machine.UpdateLocomotion(MovementMode.Sprint, MovementMode.Walk, true);
            TestAssert.Equal(PlayerPresentationState.Death, machine.CurrentState,
                "Death did not override reload and locomotion.");
            machine.ObserveDeath();
            TestAssert.Equal(1, machine.HitPresentationCount,
                "Death publication duplicated the prior hit reaction.");
        });

        await context.RunAsync(
            "completed-health-events-drive-one-hit-and-latched-death",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(3);
                int hitCountBefore =
                    main.PlayerVisual.Presentation.HitPresentationCount;

                HealthChangeResult hit = main.Player.Health.ApplyDamage(
                    new DamageInfo(5, source: null, damageKind: "Presentation test"));
                await context.WaitProcessFramesAsync(2);

                TestAssert.True(hit.Changed && !hit.CausedDeath,
                    "Non-lethal presentation damage fixture did not commit.");
                TestAssert.Equal(
                    hitCountBefore + 1,
                    main.PlayerVisual.Presentation.HitPresentationCount,
                    "One completed damage event produced duplicate hit reactions.");
                TestAssert.Equal(
                    PlayerPresentationState.HitReaction,
                    main.PlayerVisual.CurrentState,
                    "Completed damage did not select HitReaction.");

                HealthChangeResult death = main.Player.Health.ApplyDamage(
                    new DamageInfo(1000, source: null, damageKind: "Presentation test"));
                await context.WaitProcessFramesAsync(2);
                TestAssert.True(death.CausedDeath,
                    "Lethal presentation damage fixture did not commit death.");
                TestAssert.Equal(
                    PlayerPresentationState.Death,
                    main.PlayerVisual.CurrentState,
                    "Completed death did not override HitReaction.");
                TestAssert.False(main.Player.CanAcceptGameplayInput,
                    "Death presentation path re-enabled gameplay input.");
            });

        context.Run("terminal-presentation-is-latched-and-cannot-reenable-locomotion", () =>
        {
            PlayerPresentationStateMachine machine = CreateStateMachine();
            machine.UpdateLocomotion(MovementMode.Sprint, MovementMode.Walk, true);
            machine.SetPresentationAvailability(
                presentationEnabled: false,
                terminal: true,
                dead: false);
            machine.SetPresentationAvailability(
                presentationEnabled: true,
                terminal: false,
                dead: false);
            machine.UpdateLocomotion(MovementMode.Walk, MovementMode.Walk, true);

            TestAssert.True(machine.IsTerminal,
                "Terminal presentation state was cleared.");
            TestAssert.Equal(PlayerPresentationState.Disabled, machine.CurrentState,
                "Terminal state re-enabled locomotion presentation.");
            TestAssert.False(machine.ObserveCompletedShot(),
                "Terminal presentation accepted a completed combat action.");
        });

        await context.RunAsync(
            "socket-hierarchy-fallback-and-animation-absence-are-safe",
            async () =>
            {
                PlayerController3D player =
                    context.InstantiateScene<PlayerController3D>(
                        "res://scenes/3d/player/Player3D.tscn");
                await context.WaitProcessFramesAsync(2);
                PlayerVisualController3D visual = player.Visual;

                TestAssert.True(visual.HasValidSocketHierarchy,
                    "PlayerVisual3D socket references are invalid.");
                TestAssert.Same(visual.WeaponSocket, visual.MuzzleSocket.GetParent(),
                    "MuzzleSocket is outside the weapon visual hierarchy.");
                TestAssert.Same(visual.WeaponSocket, visual.FlashlightSocket.GetParent(),
                    "FlashlightSocket is outside the weapon visual hierarchy.");
                TestAssert.True(visual.IsUsingDevelopmentFallback,
                    "Missing player art did not enable the explicit development fallback.");
                TestAssert.False(visual.HasImportedModel,
                    "Empty imported-model root was treated as valid final art.");
                TestAssert.True(
                    visual.DevelopmentFallbackRoot.Visible !=
                    visual.ImportedModelRoot.Visible,
                    "Imported model and primitive fallback are visible together.");
                TestAssert.False(visual.IsAnimationTreeActive,
                    "AnimationTree activated without a compatible imported animation set.");
                TestAssert.Equal(11, visual.MissingClipCount,
                    "Missing optional clips were not reported explicitly.");
            });

        await context.RunAsync(
            "valid-imported-visual-disables-fallback-and-uses-player-layer",
            async () =>
            {
                PackedScene scene = ResourceLoader.Load<PackedScene>(
                    "res://scenes/3d/player/PlayerVisual3D.tscn")
                    ?? throw new TestAssertionException(
                        "Could not load PlayerVisual3D scene.");
                PlayerVisualController3D visual =
                    scene.Instantiate<PlayerVisualController3D>();
                Node3D importedRoot = visual.GetNode<Node3D>(
                    "ModelYawRoot3D/ModelAlignmentRoot3D/ImportedModelRoot3D");
                MeshInstance3D importedMesh = new()
                {
                    Name = "ImportedPlayerFixtureMesh3D",
                    Mesh = new BoxMesh(),
                    Layers = RenderLayers3D.World,
                };
                importedRoot.AddChild(importedMesh);
                context.AddNode(visual);
                await context.WaitProcessFramesAsync(2);

                TestAssert.True(visual.HasImportedModel &&
                                visual.IsImportedModelValid,
                    "Valid imported presentation fixture was rejected.");
                TestAssert.False(visual.IsUsingDevelopmentFallback,
                    "Valid imported presentation did not disable the fallback.");
                TestAssert.True(importedRoot.Visible &&
                                !visual.DevelopmentFallbackRoot.Visible,
                    "Imported and fallback visuals were not mutually exclusive.");
                TestAssert.Equal(
                    RenderLayers3D.PlayerVisual,
                    importedMesh.Layers,
                    "Imported player mesh was not isolated from the flashlight layer.");
            });

        await context.RunAsync(
            "presentation-and-animation-callbacks-do-not-mutate-gameplay-models",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(3);
                int healthBefore = main.Player.Health.CurrentHealth;
                int ammoBefore = main.Weapon.State.CurrentMagazineAmmo;
                double staminaBefore = main.Player.Stamina.Current;
                Vector3 positionBefore = main.Player.GlobalPosition;
                PlayerPresentationStateMachine presentation =
                    main.PlayerVisual.Presentation;

                presentation.ObserveCompletedShot();
                presentation.ObserveReload(ReloadStatus.Started);
                presentation.ObserveCompletedDamage(
                    changed: true,
                    causedDeath: false);
                AssertNoMethodAnimationTracks(main.PlayerVisual.AnimationPlayer);

                TestAssert.Equal(healthBefore, main.Player.Health.CurrentHealth,
                    "Presentation changed player health.");
                TestAssert.Equal(ammoBefore, main.Weapon.State.CurrentMagazineAmmo,
                    "Presentation changed firearm ammunition.");
                TestAssert.NearlyEqual(
                    staminaBefore,
                    main.Player.Stamina.Current,
                    Tolerance,
                    "Presentation changed stamina.");
                AssertVectorNearlyEqual(positionBefore, main.Player.GlobalPosition,
                    "Presentation moved the CharacterBody3D through root motion.");
            });

        await context.RunAsync(
            "player-visual-layer-cannot-self-shadow-flashlight",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(2);
                SpotLight3D flashlight =
                    main.PlayerVisual.FlashlightController.GetNode<SpotLight3D>(
                        "%FlashlightSpotLight3D");
                AssertVisualLayersExcludeLight(main.PlayerVisual, flashlight.LightCullMask);
                TestAssert.Equal(RenderLayers3D.World, flashlight.LightCullMask,
                    "Flashlight no longer illuminates the world render layer.");
                TestAssert.True(flashlight.ShadowEnabled,
                    "Flashlight no longer stops at solid world walls.");
            });

        await context.RunAsync(
            "main3d-and-legacy-2d-scenes-load-with-presentation-pipeline",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(3);
                TestAssert.True(main.IsInitialized,
                    "Main3D did not initialize the player presentation pipeline.");
                TestAssert.Same(main.Player.Visual, main.PlayerVisual,
                    "Main3D composed a different player visual adapter.");

                Main legacy = context.InstantiateScene<Main>(
                    "res://scenes/main/Main.tscn");
                await context.WaitProcessFramesAsync(3);
                TestAssert.True(legacy.IsInitialized,
                    "Legacy 2D composition no longer loads.");
            });
    }

    private static PlayerPresentationStateMachine CreateStateMachine()
    {
        return new PlayerPresentationStateMachine(
            firePresentationSeconds: 0.12,
            hitReactionSeconds: 0.22);
    }

    private static void AssertBlend(
        Vector3 velocity,
        Vector2 expected,
        string message)
    {
        PlayerLocomotionBlendResult result = PlayerLocomotionBlend3D.Calculate(
            velocity,
            Vector3.Forward,
            Vector3.Right,
            5.0f,
            0.05f,
            0.12f,
            wasMoving: false);
        TestAssert.NearlyEqual(expected.X, result.LocalBlend.X, Tolerance, message);
        TestAssert.NearlyEqual(expected.Y, result.LocalBlend.Y, Tolerance, message);
    }

    private static bool HasCollisionDescendant(Node node)
    {
        for (int index = 0; index < node.GetChildCount(); index++)
        {
            Node child = node.GetChild(index);
            if (child is CollisionObject3D or CollisionShape3D ||
                HasCollisionDescendant(child))
            {
                return true;
            }
        }

        return false;
    }

    private static StaticBody3D CreateCeiling(Vector3 position)
    {
        StaticBody3D ceiling = new()
        {
            Name = "PresentationClearanceCeiling3D",
            Position = position,
            CollisionLayer = CollisionLayers3D.World,
            CollisionMask = 0,
        };
        ceiling.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(4.0f, 0.2f, 4.0f),
            },
        });
        return ceiling;
    }

    private static void AssertNoMethodAnimationTracks(AnimationPlayer player)
    {
        string[] animationNames = player.GetAnimationList();
        for (int animationIndex = 0;
             animationIndex < animationNames.Length;
             animationIndex++)
        {
            Animation animation = player.GetAnimation(animationNames[animationIndex]);
            for (int trackIndex = 0;
                 trackIndex < animation.GetTrackCount();
                 trackIndex++)
            {
                TestAssert.True(
                    animation.TrackGetType(trackIndex) != Animation.TrackType.Method,
                    $"Animation '{animationNames[animationIndex]}' contains a gameplay-capable method track.");
            }
        }
    }

    private static void AssertVisualLayersExcludeLight(
        Node node,
        uint lightCullMask)
    {
        for (int index = 0; index < node.GetChildCount(); index++)
        {
            Node child = node.GetChild(index);
            if (child is MeshInstance3D mesh)
            {
                TestAssert.Equal(
                    0u,
                    mesh.Layers & lightCullMask,
                    $"Player visual '{mesh.Name}' can self-shadow the flashlight.");
            }

            AssertVisualLayersExcludeLight(child, lightCullMask);
        }
    }

    private static void AssertVectorNearlyEqual(
        Vector3 expected,
        Vector3 actual,
        string message)
    {
        TestAssert.NearlyEqual(expected.X, actual.X, Tolerance, message);
        TestAssert.NearlyEqual(expected.Y, actual.Y, Tolerance, message);
        TestAssert.NearlyEqual(expected.Z, actual.Z, Tolerance, message);
    }
}
