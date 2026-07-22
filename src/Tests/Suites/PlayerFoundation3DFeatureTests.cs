using System;
using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Gameplay.Movement;
using LineZero.Tests.Framework;
using LineZero.UI;
using LineZero.World3D;

namespace LineZero.Tests.Suites;

public sealed class PlayerFoundation3DFeatureTests : IFeatureTestSuite
{
    private const double Tolerance = 0.0001;

    public string Id => "player-foundation-3d";

    public string Description =>
        "Stage 3D-02 movement, posture, sensors, camera, occlusion, and debug contracts";

    public async Task RunAsync(FeatureTestContext context)
    {
        context.Run("cardinal-and-diagonal-speed-and-acceleration-are-equal", () =>
        {
            const float configuredSpeed = 6.25f;
            Vector3 cardinal = GroundMovement3D.CalculateTargetVelocity(
                Vector2.Up,
                Vector3.Forward,
                Vector3.Right,
                configuredSpeed);
            Vector3 diagonal = GroundMovement3D.CalculateTargetVelocity(
                new Vector2(1.0f, -1.0f),
                Vector3.Forward,
                Vector3.Right,
                configuredSpeed);

            TestAssert.NearlyEqual(configuredSpeed, cardinal.Length(), Tolerance,
                "Cardinal target speed did not equal the configured speed.");
            TestAssert.NearlyEqual(cardinal.Length(), diagonal.Length(), Tolerance,
                "Diagonal target speed exceeded cardinal target speed.");

            Vector3 cardinalAcceleration =
                GroundMovement3D.MoveHorizontalVelocityToward(
                    Vector3.Zero,
                    cardinal,
                    0.75f);
            Vector3 diagonalAcceleration =
                GroundMovement3D.MoveHorizontalVelocityToward(
                    Vector3.Zero,
                    diagonal,
                    0.75f);
            TestAssert.NearlyEqual(
                cardinalAcceleration.Length(),
                diagonalAcceleration.Length(),
                Tolerance,
                "Diagonal acceleration exceeded cardinal acceleration.");
        });

        await context.RunAsync(
            "sprint-drain-cancels-on-release-and-disabled-input",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(8);

                Input.ActionPress("move_up");
                Input.ActionPress("sprint");
                try
                {
                    await context.WaitPhysicsFramesAsync(20);
                    double staminaAfterDrain = main.Player.Stamina.Current;
                    TestAssert.True(
                        staminaAfterDrain < main.Player.Stamina.Maximum,
                        "3D Sprint did not drain the shared stamina model.");

                    Input.ActionRelease("sprint");
                    await context.WaitPhysicsFramesAsync(12);
                    TestAssert.NearlyEqual(
                        staminaAfterDrain,
                        main.Player.Stamina.Current,
                        Tolerance,
                        "Stamina continued draining after Sprint release.");
                    TestAssert.False(
                        main.Player.CurrentMovementMode == MovementMode.Sprint,
                        "Sprint remained active after its input was released.");

                    Input.ActionPress("sprint");
                    await context.WaitPhysicsFramesAsync(8);
                    double staminaBeforeDisable = main.Player.Stamina.Current;
                    main.SetGameplayInputEnabled(false);
                    await context.WaitPhysicsFramesAsync(8);
                    TestAssert.NearlyEqual(
                        staminaBeforeDisable,
                        main.Player.Stamina.Current,
                        Tolerance,
                        "Disabled gameplay input did not cancel Sprint drain.");
                    TestAssert.NearlyEqual(
                        0.0,
                        main.Player.HorizontalVelocity.Length(),
                        Tolerance,
                        "Disabled gameplay input did not stop horizontal motion.");
                }
                finally
                {
                    Input.ActionRelease("sprint");
                    Input.ActionRelease("move_up");
                }
            });

        await context.RunAsync(
            "non-standing-postures-promote-to-walk-when-sprint-begins",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(8);

                await AssertSprintPromotesToWalkAsync(
                    context,
                    main,
                    MovementMode.Crawl);
                await AssertSprintPromotesToWalkAsync(
                    context,
                    main,
                    MovementMode.Crouch);
            });

        await context.RunAsync(
            "standing-crouch-and-crawl-use-distinct-collision-profiles",
            async () =>
            {
                PlayerController3D player =
                    context.InstantiateScene<PlayerController3D>(
                        "res://scenes/3d/player/Player3D.tscn");
                await context.WaitPhysicsFramesAsync(2);

                AssertActiveCollisionProfile(player, MovementMode.Walk);
                TestAssert.True(player.TrySetPosture(MovementMode.Crouch),
                    "Player could not enter Crouch.");
                AssertActiveCollisionProfile(player, MovementMode.Crouch);
                TestAssert.True(player.TrySetPosture(MovementMode.Crawl),
                    "Player could not enter Crawl.");
                AssertActiveCollisionProfile(player, MovementMode.Crawl);
                TestAssert.True(player.TrySetPosture(MovementMode.Crouch),
                    "Player could not leave Crawl for Crouch in open space.");
                AssertActiveCollisionProfile(player, MovementMode.Crouch);
                TestAssert.True(player.TrySetPosture(MovementMode.Walk),
                    "Player could not leave Crouch for Walk in open space.");
                AssertActiveCollisionProfile(player, MovementMode.Walk);
            });

        await context.RunAsync(
            "space-stand-up-and-posture-visual-scaling-keep-equipment-stable",
            async () =>
            {
                PlayerController3D player =
                    context.InstantiateScene<PlayerController3D>(
                        "res://scenes/3d/player/Player3D.tscn");
                await context.WaitPhysicsFramesAsync(2);

                Node3D visualPivot = player.GetNode<Node3D>("%VisualPivot3D");
                Node3D postureVisuals = player.GetNode<Node3D>("%PostureVisuals3D");
                Node3D flashlight = player.GetNode<Node3D>(
                    "%PlayerFlashlightController3D");
                Marker3D muzzle = player.GetNode<Marker3D>("%MuzzlePoint3D");
                Vector3 originalFlashlightPosition = flashlight.Position;
                Vector3 originalMuzzlePosition = muzzle.Position;
                Vector3 originalVisualPivotScale = visualPivot.Scale;

                TestAssert.True(player.TrySetPosture(MovementMode.Crawl),
                    "Player could not enter Crawl for the stand-up fixture.");
                player._UnhandledInput(Action("stand_up"));
                await context.WaitPhysicsFramesAsync(2);

                TestAssert.Equal(MovementMode.Walk, player.CurrentPosture,
                    "Space stand-up input did not restore the standing posture.");
                AssertVectorNearlyEqual(originalFlashlightPosition, flashlight.Position,
                    "Posture changes moved the flashlight origin and can destabilize shadows.");
                AssertVectorNearlyEqual(originalMuzzlePosition, muzzle.Position,
                    "Posture changes moved the muzzle origin unexpectedly.");
                AssertVectorNearlyEqual(originalVisualPivotScale, visualPivot.Scale,
                    "The shared visual pivot still scales equipment and lighting.");
                TestAssert.True(postureVisuals.Scale.Y > 0.45f &&
                                postureVisuals.Scale.Y <= 1.0f,
                    "Posture-only presentation root has no bounded posture scaling.");

                TestAssert.True(player.TrySetPosture(MovementMode.Crouch),
                    "Player could not re-enter Crouch.");
                await context.WaitPhysicsFramesAsync(1);
                AssertVectorNearlyEqual(originalFlashlightPosition, flashlight.Position,
                    "Crouch changed the flashlight local origin.");
                AssertVectorNearlyEqual(originalMuzzlePosition, muzzle.Position,
                    "Crouch changed the muzzle local origin.");
                TestAssert.True(postureVisuals.Scale.Y < 1.0f,
                    "Crouch did not shrink only the posture visuals.");
            });

        await context.RunAsync(
            "clearance-blocks-crawl-to-crouch-and-crouch-to-walk",
            async () =>
            {
                PlayerController3D player =
                    context.InstantiateScene<PlayerController3D>(
                        "res://scenes/3d/player/Player3D.tscn");
                await context.WaitPhysicsFramesAsync(2);
                TestAssert.True(player.TrySetPosture(MovementMode.Crawl),
                    "Player could not enter Crawl for clearance coverage.");

                StaticBody3D ceiling = CreateCeiling(1.15f);
                context.AddNode(ceiling);
                await context.WaitPhysicsFramesAsync(2);
                TestAssert.False(
                    player.TrySetPosture(
                        MovementMode.Crouch,
                        notifyOnFailure: false),
                    "Player left Crawl when the Crouch shape was blocked.");
                TestAssert.Equal(MovementMode.Crawl, player.CurrentPosture,
                    "Blocked Crawl-to-Crouch transition changed posture.");

                ceiling.Position = new Vector3(0.0f, 1.55f, 0.0f);
                await context.WaitPhysicsFramesAsync(2);
                TestAssert.True(
                    player.TrySetPosture(
                        MovementMode.Crouch,
                        notifyOnFailure: false),
                    "Crouch shape did not fit below a valid low ceiling.");
                TestAssert.False(
                    player.TrySetPosture(
                        MovementMode.Walk,
                        notifyOnFailure: false),
                    "Player left Crouch when the standing shape was blocked.");
                TestAssert.Equal(MovementMode.Crouch, player.CurrentPosture,
                    "Blocked Crouch-to-Walk transition changed posture.");

                ceiling.Position = new Vector3(0.0f, 3.0f, 0.0f);
                await context.WaitPhysicsFramesAsync(2);
                TestAssert.True(
                    player.TrySetPosture(
                        MovementMode.Walk,
                        notifyOnFailure: false),
                    "Standing shape did not fit after clearance was restored.");
            });

        await context.RunAsync(
            "all-gameplay-sensors-remain-constant-across-postures",
            async () =>
            {
                PlayerController3D player =
                    context.InstantiateScene<PlayerController3D>(
                        "res://scenes/3d/player/Player3D.tscn");
                await context.WaitPhysicsFramesAsync(2);
                SensorSnapshot[] sensors =
                {
                    CaptureSensor(
                        player,
                        "%PlayerInteractionSensor3D",
                        "%PlayerInteractionSensorShape3D"),
                    CaptureSensor(
                        player,
                        "%PlayerHazardSensor3D",
                        "%PlayerHazardSensorShape3D"),
                    CaptureSensor(
                        player,
                        "%PlayerVisibilitySensor3D",
                        "%PlayerVisibilitySensorShape3D"),
                    CaptureSensor(
                        player,
                        "%PlayerObjectiveSensor3D",
                        "%PlayerObjectiveSensorShape3D")
                };

                TestAssert.True(player.TrySetPosture(MovementMode.Crouch),
                    "Player could not enter Crouch for sensor coverage.");
                AssertSensorsUnchanged(sensors, "Crouch");
                TestAssert.True(player.TrySetPosture(MovementMode.Crawl),
                    "Player could not enter Crawl for sensor coverage.");
                AssertSensorsUnchanged(sensors, "Crawl");
                TestAssert.True(player.TrySetPosture(MovementMode.Crouch),
                    "Player could not leave Crawl for sensor coverage.");
                TestAssert.True(player.TrySetPosture(MovementMode.Walk),
                    "Player could not leave Crouch for sensor coverage.");
                AssertSensorsUnchanged(sensors, "Walk");
            });

        await context.RunAsync(
            "aim-rotation-does-not-change-camera-relative-movement",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(3);
                TopDownCamera3D camera = main.GetNode<TopDownCamera3D>(
                    "%TopDownCamera3D");
                PlayerAimController3D aim =
                    main.Player.GetNode<PlayerAimController3D>(
                        "%PlayerAimController3D");
                Node3D visualPivot = main.Player.GetNode<Node3D>("%VisualPivot3D");
                Vector3 fixedCameraRotation = camera.GlobalRotation;
                Basis cameraBasis = camera.GlobalTransform.Basis;
                Vector3 movementBefore = GroundMovement3D.CalculateTargetVelocity(
                    Vector2.Up,
                    -cameraBasis.Z,
                    cameraBasis.X,
                    main.Player.WalkingSpeed);

                aim.SetProcess(false);
                TestAssert.True(
                    aim.TryApplyWorldAimPoint(
                        main.Player.GlobalPosition + (Vector3.Forward * 6.0f)),
                    "First independent aim point was rejected.");
                float firstYaw = visualPivot.GlobalRotation.Y;
                TestAssert.True(
                    aim.TryApplyWorldAimPoint(
                        main.Player.GlobalPosition + (Vector3.Right * 6.0f)),
                    "Second independent aim point was rejected.");
                float secondYaw = visualPivot.GlobalRotation.Y;
                Vector3 movementAfter = GroundMovement3D.CalculateTargetVelocity(
                    Vector2.Up,
                    -cameraBasis.Z,
                    cameraBasis.X,
                    main.Player.WalkingSpeed);

                TestAssert.True(Mathf.Abs(firstYaw - secondYaw) > 0.1f,
                    "Mouse aiming did not rotate the visual pivot around Y.");
                AssertVectorNearlyEqual(movementBefore, movementAfter,
                    "Aim rotation changed camera-relative movement.");
                AssertVectorNearlyEqual(fixedCameraRotation, camera.GlobalRotation,
                    "Aim rotation changed the fixed camera rotation.");
            });

        await context.RunAsync(
            "multiple-occluders-fade-restore-and-keep-collision",
            async () =>
            {
                Node3D root = context.AddNode(new Node3D
                {
                    Name = "CameraOcclusionFixture3D"
                });
                CharacterBody3D target = new()
                {
                    Name = "Target3D",
                    CollisionLayer = CollisionLayers3D.PlayerMovementBody,
                    CollisionMask = 0
                };
                target.AddChild(new CollisionShape3D
                {
                    Shape = new CapsuleShape3D
                    {
                        Radius = 0.5f,
                        Height = 1.8f
                    },
                    Position = new Vector3(0.0f, 0.9f, 0.0f)
                });
                root.AddChild(target);
                Camera3D camera = new()
                {
                    Name = "Camera3D",
                    Position = new Vector3(0.0f, 4.0f, 8.0f)
                };
                root.AddChild(camera);
                CameraOcclusionController3D controller = new()
                {
                    Name = "CameraOcclusionController3D"
                };
                root.AddChild(controller);
                CameraOccluder3D first = CreateOccluder(
                    "FirstOccluder3D",
                    new Vector3(0.0f, 3.0f, 5.0f),
                    out MeshInstance3D firstMesh,
                    out CollisionShape3D firstCollision);
                CameraOccluder3D second = CreateOccluder(
                    "SecondOccluder3D",
                    new Vector3(0.0f, 3.0f, 3.0f),
                    out MeshInstance3D secondMesh,
                    out CollisionShape3D secondCollision);
                root.AddChild(first);
                root.AddChild(second);
                await context.WaitPhysicsFramesAsync(2);

                controller.Bind(camera, target);
                controller.SetPhysicsProcess(false);
                controller.RefreshOcclusion();
                first._Process(1.0);
                second._Process(1.0);
                TestAssert.True(first.IsOccluded && second.IsOccluded,
                    "Camera query did not fade both simultaneous occluders.");
                StandardMaterial3D firstFadeMaterial =
                    firstMesh.GetActiveMaterial(0) as StandardMaterial3D
                    ?? throw new TestAssertionException(
                        "First occluder installed no material-alpha fade.");
                StandardMaterial3D secondFadeMaterial =
                    secondMesh.GetActiveMaterial(0) as StandardMaterial3D
                    ?? throw new TestAssertionException(
                        "Second occluder installed no material-alpha fade.");
                TestAssert.True(
                    firstFadeMaterial.AlbedoColor.A < 1.0f &&
                    secondFadeMaterial.AlbedoColor.A < 1.0f,
                    "Occluded meshes did not apply material alpha.");
                TestAssert.NearlyEqual(0.0, firstMesh.Transparency, Tolerance,
                    "First occluder used unsupported geometry transparency.");
                TestAssert.NearlyEqual(0.0, secondMesh.Transparency, Tolerance,
                    "Second occluder used unsupported geometry transparency.");
                TestAssert.False(
                    firstCollision.Disabled || secondCollision.Disabled,
                    "Camera fading disabled physical collision.");
                TestAssert.True(
                    (first.CollisionLayer & CollisionLayers3D.World) != 0 &&
                    (second.CollisionLayer & CollisionLayers3D.World) != 0,
                    "Camera fading removed the world collision layer.");

                System.Reflection.PropertyInfo? countProperty =
                    typeof(CameraOcclusionController3D).GetProperty(
                        "FadedOccluderCount");
                TestAssert.True(countProperty is not null,
                    "Camera occlusion controller exposes no faded-count state.");
                if (countProperty is not null)
                {
                    TestAssert.Equal(2, (int)countProperty.GetValue(controller)!,
                        "Faded occluder count did not match active visuals.");
                }

                first.Position += Vector3.Right * 5.0f;
                second.Position += Vector3.Right * 5.0f;
                await context.WaitPhysicsFramesAsync(2);
                controller.RefreshOcclusion();
                controller.RefreshOcclusion();
                first._Process(1.0);
                second._Process(1.0);
                TestAssert.False(first.IsOccluded || second.IsOccluded,
                    "Cleared occluders did not restore their visible state.");
                TestAssert.NearlyEqual(0.0, firstMesh.Transparency, Tolerance,
                    "First occluder changed geometry transparency.");
                TestAssert.NearlyEqual(0.0, secondMesh.Transparency, Tolerance,
                    "Second occluder changed geometry transparency.");
                TestAssert.False(
                    firstCollision.Disabled || secondCollision.Disabled,
                    "Restoring visuals changed physical collision.");
            });

        await context.RunAsync(
            "test-level-authors-low-ceiling-and-crawl-only-passage",
            async () =>
            {
                Node3D level = context.InstantiateScene<Node3D>(
                    "res://scenes/3d/levels/TestLevel3D.tscn");
                PlayerController3D player =
                    context.InstantiateScene<PlayerController3D>(
                        "res://scenes/3d/player/Player3D.tscn");
                await context.WaitProcessFramesAsync(2);

                CollisionShape3D standingCollision =
                    player.GetNode<CollisionShape3D>("%NormalCollisionShape3D");
                CollisionShape3D crouchCollision =
                    player.GetNode<CollisionShape3D>("%CrouchCollisionShape3D");
                CollisionShape3D crawlCollision =
                    player.GetNode<CollisionShape3D>("%CrawlCollisionShape3D");
                float standingTop = GetCapsuleTop(standingCollision);
                float crouchTop = GetCapsuleTop(crouchCollision);
                float crawlTop = GetCapsuleTop(crawlCollision);
                CollisionShape3D lowCeiling = level.GetNode<CollisionShape3D>(
                    "%LowCeiling3D/CollisionShape3D");
                CollisionShape3D crawlOnlyOverhead =
                    level.GetNode<CollisionShape3D>(
                        "%CrawlOnlyOverhead3D/CollisionShape3D");
                float lowCeilingBottom = GetBoxBottom(lowCeiling);
                float crawlOnlyBottom = GetBoxBottom(crawlOnlyOverhead);

                TestAssert.True(
                    lowCeilingBottom > crouchTop &&
                    lowCeilingBottom < standingTop,
                    "Low ceiling does not allow Crouch while blocking Walk.");
                TestAssert.True(
                    crawlOnlyBottom > crawlTop &&
                    crawlOnlyBottom < crouchTop,
                    "Crawl-only passage does not allow Crawl while blocking Crouch.");

                CameraOccluder3D southWall =
                    level.GetNode<CameraOccluder3D>("%WallSouth3D");
                CollisionShape3D southWallCollision =
                    southWall.GetNode<CollisionShape3D>("CollisionShape3D");
                TestAssert.True(
                    (southWall.CollisionLayer & CollisionLayers3D.World) != 0 &&
                    (southWall.CollisionLayer &
                     CollisionLayers3D.CameraOccluder) != 0,
                    "Designated camera wall does not retain world collision.");
                southWall.SetOccluded(true);
                TestAssert.False(southWallCollision.Disabled,
                    "Fading the designated camera wall disabled collision.");
                southWall.SetOccluded(false);
            });

        await context.RunAsync(
            "debug-hud-exposes-event-driven-player-foundation-state",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(2);
                DebugHud3D hud = main.GetNode<DebugHud3D>("%DebugHud3D");
                Label label = hud.GetNode<Label>("%StatsLabel3D");

                TestAssert.False(hud.IsProcessing(),
                    "Debug HUD polls gameplay state every process frame.");
                AssertContains(label.Text, "Movement mode:");
                AssertContains(label.Text, "Stamina:");
                AssertContains(label.Text, "Posture:");
                AssertContains(label.Text, "Clearance:");
                AssertContains(label.Text, "Gameplay input enabled:");
                AssertContains(label.Text, "Terminal:");
                AssertContains(label.Text, "Faded occluders:");

                TestAssert.True(main.Player.TrySetPosture(MovementMode.Crouch),
                    "Debug HUD fixture could not enter Crouch.");
                AssertContains(label.Text, "Movement mode: Crouch");
                AssertContains(label.Text, "Posture: Crouch");
                main.SetGameplayInputEnabled(false);
                AssertContains(label.Text, "Gameplay input enabled: False");
            });

        await context.RunAsync("main3d-and-legacy-2d-scenes-load", async () =>
        {
            Main3D main3D = context.InstantiateScene<Main3D>(
                "res://scenes/3d/Main3D.tscn");
            await context.WaitProcessFramesAsync(2);
            TestAssert.True(main3D.IsInitialized,
                "Main3D did not complete composition.");

            Node legacyMain = context.InstantiateScene<Node>(
                "res://scenes/main/Main.tscn");
            await context.WaitProcessFramesAsync(2);
            TestAssert.True(legacyMain.IsInsideTree(),
                "Legacy 2D main scene did not enter the scene tree.");
        });
    }

    private static InputEventAction Action(string action)
    {
        return new InputEventAction
        {
            Action = action,
            Pressed = true,
            Strength = 1.0f
        };
    }

    private static async Task AssertSprintPromotesToWalkAsync(
        FeatureTestContext context,
        Main3D main,
        MovementMode startingPosture)
    {
        TestAssert.True(main.Player.TrySetPosture(startingPosture),
            $"Player could not enter {startingPosture} for the sprint-promotion fixture.");
        await context.WaitPhysicsFramesAsync(2);
        double staminaBefore = main.Player.Stamina.Current;

        main.Player._UnhandledInput(Action("sprint"));
        Input.ActionPress("move_up");
        Input.ActionPress("sprint");
        try
        {
            await context.WaitPhysicsFramesAsync(12);
        }
        finally
        {
            Input.ActionRelease("sprint");
            Input.ActionRelease("move_up");
        }

        TestAssert.Equal(MovementMode.Walk, main.Player.CurrentPosture,
            $"Sprint input did not lift the player out of {startingPosture}.");
        TestAssert.True(
            main.Player.CurrentMovementMode == MovementMode.Sprint ||
            main.Player.CurrentMovementMode == MovementMode.Walk,
            $"Sprint promotion from {startingPosture} never reached a standing movement state.");
        TestAssert.True(main.Player.Stamina.Current < staminaBefore,
            $"Sprint promotion from {startingPosture} did not consume stamina.");
    }

    private static StaticBody3D CreateCeiling(float centerHeight)
    {
        StaticBody3D ceiling = new()
        {
            Name = "PostureClearanceCeiling3D",
            CollisionLayer = CollisionLayers3D.World,
            CollisionMask = 0,
            Position = new Vector3(0.0f, centerHeight, 0.0f)
        };
        ceiling.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(4.0f, 0.2f, 4.0f)
            }
        });
        return ceiling;
    }

    private static CameraOccluder3D CreateOccluder(
        string name,
        Vector3 position,
        out MeshInstance3D mesh,
        out CollisionShape3D collision)
    {
        CameraOccluder3D occluder = new()
        {
            Name = name,
            Position = position,
            CollisionLayer = CollisionLayers3D.World |
                             CollisionLayers3D.CameraOccluder,
            CollisionMask = 0
        };
        mesh = new MeshInstance3D
        {
            Name = "OccluderMesh3D",
            Mesh = new BoxMesh
            {
                Size = new Vector3(2.0f, 6.0f, 0.3f)
            }
        };
        collision = new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = new BoxShape3D
            {
                Size = new Vector3(2.0f, 6.0f, 0.3f)
            }
        };
        occluder.AddChild(mesh);
        occluder.AddChild(collision);
        return occluder;
    }

    private static void AssertActiveCollisionProfile(
        PlayerController3D player,
        MovementMode expectedPosture)
    {
        CollisionShape3D standing =
            player.GetNode<CollisionShape3D>("%NormalCollisionShape3D");
        CollisionShape3D crouch =
            player.GetNode<CollisionShape3D>("%CrouchCollisionShape3D");
        CollisionShape3D crawl =
            player.GetNode<CollisionShape3D>("%CrawlCollisionShape3D");
        TestAssert.Equal(expectedPosture != MovementMode.Walk, standing.Disabled,
            "Standing collision activation did not match posture.");
        TestAssert.Equal(expectedPosture != MovementMode.Crouch, crouch.Disabled,
            "Crouch collision activation did not match posture.");
        TestAssert.Equal(expectedPosture != MovementMode.Crawl, crawl.Disabled,
            "Crawl collision activation did not match posture.");
    }

    private static SensorSnapshot CaptureSensor(
        PlayerController3D player,
        string areaPath,
        string shapePath)
    {
        Area3D area = player.GetNode<Area3D>(areaPath);
        CollisionShape3D collision = area.GetNode<CollisionShape3D>(shapePath);
        Shape3D shape = collision.Shape
            ?? throw new TestAssertionException(
                $"Sensor '{shapePath}' has no shape resource.");
        return new SensorSnapshot(
            area,
            collision,
            shape,
            collision.Transform,
            area.CollisionLayer,
            area.CollisionMask);
    }

    private static void AssertSensorsUnchanged(
        SensorSnapshot[] sensors,
        string postureName)
    {
        for (int index = 0; index < sensors.Length; index++)
        {
            SensorSnapshot snapshot = sensors[index];
            Shape3D currentShape = snapshot.Collision.Shape
                ?? throw new TestAssertionException(
                    $"{postureName} removed a gameplay sensor shape.");
            TestAssert.Same(snapshot.Shape, currentShape,
                $"{postureName} replaced a gameplay sensor shape.");
            TestAssert.Equal(snapshot.Transform, snapshot.Collision.Transform,
                $"{postureName} changed a gameplay sensor transform.");
            TestAssert.Equal(snapshot.CollisionLayer, snapshot.Area.CollisionLayer,
                $"{postureName} changed a gameplay sensor layer.");
            TestAssert.Equal(snapshot.CollisionMask, snapshot.Area.CollisionMask,
                $"{postureName} changed a gameplay sensor mask.");
            TestAssert.False(snapshot.Collision.Disabled,
                $"{postureName} disabled a gameplay sensor shape.");
        }
    }

    private static float GetCapsuleTop(CollisionShape3D collision)
    {
        CapsuleShape3D capsule = collision.Shape as CapsuleShape3D
            ?? throw new TestAssertionException(
                $"'{collision.Name}' is not a capsule shape.");
        return collision.Position.Y + (capsule.Height * 0.5f);
    }

    private static float GetBoxBottom(CollisionShape3D collision)
    {
        BoxShape3D box = collision.Shape as BoxShape3D
            ?? throw new TestAssertionException(
                $"'{collision.Name}' is not a box shape.");
        return collision.GlobalPosition.Y - (box.Size.Y * 0.5f);
    }

    private static void AssertContains(string value, string expectedSubstring)
    {
        TestAssert.True(
            value.Contains(expectedSubstring, StringComparison.Ordinal),
            $"Debug HUD text does not contain '{expectedSubstring}'.");
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

    private readonly record struct SensorSnapshot(
        Area3D Area,
        CollisionShape3D Collision,
        Shape3D Shape,
        Transform3D Transform,
        uint CollisionLayer,
        uint CollisionMask);
}
