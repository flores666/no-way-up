using System;
using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Gameplay.Movement;
using LineZero.Tests.Framework;
using LineZero.World3D;
using LineZero.World3D.Enemies;
using LineZero.World3D.Hazards;
using LineZero.World3D.Interaction;
using LineZero.World3D.Items;
using LineZero.World3D.Objectives;
using LineZero.World3D.Perception;
using LineZero.World3D.Power;

namespace LineZero.Tests.Suites;

public sealed class Foundation3DFeatureTests : IFeatureTestSuite
{
    private const double Tolerance = 0.0001;

    public string Id => "foundation-3d";

    public string Description =>
        "Pure XZ movement/aim math and parallel 3D scene composition contracts";

    public async Task RunAsync(FeatureTestContext context)
    {
        context.Run("movement-normalizes-diagonal-and-preserves-zero", () =>
        {
            Vector3 diagonal = GroundMovement3D.CalculateCameraRelativeDirection(
                new Vector2(1.0f, -1.0f),
                Vector3.Forward,
                Vector3.Right);
            Vector3 zero = GroundMovement3D.CalculateCameraRelativeDirection(
                Vector2.Zero,
                Vector3.Forward,
                Vector3.Right);

            TestAssert.NearlyEqual(1.0, diagonal.Length(), Tolerance,
                "Diagonal 3D movement was not normalized.");
            TestAssert.NearlyEqual(0.0, diagonal.Y, Tolerance,
                "Ground movement introduced vertical velocity.");
            TestAssert.Equal(Vector3.Zero, zero,
                "Zero 3D input produced a movement direction.");
        });

        context.Run("camera-relative-movement-ignores-vertical-basis", () =>
        {
            Vector3 flat = GroundMovement3D.CalculateCameraRelativeDirection(
                Vector2.Up,
                new Vector3(0.0f, 0.0f, -1.0f),
                Vector3.Right);
            Vector3 tilted = GroundMovement3D.CalculateCameraRelativeDirection(
                Vector2.Up,
                new Vector3(0.0f, -20.0f, -1.0f),
                new Vector3(1.0f, 7.0f, 0.0f));

            AssertVectorNearlyEqual(flat, tilted,
                "Vertical camera components changed screen-relative movement.");
            AssertVectorNearlyEqual(Vector3.Forward, tilted,
                "Move-up no longer points toward the top of the camera view.");
        });

        context.Run("cardinal-and-diagonal-acceleration-have-equal-magnitude", () =>
        {
            Vector3 cardinalTarget = new(6.0f, 0.0f, 0.0f);
            Vector3 diagonalTarget = new Vector3(1.0f, 0.0f, 1.0f).Normalized() * 6.0f;
            Vector3 cardinal = GroundMovement3D.MoveHorizontalVelocityToward(
                Vector3.Zero,
                cardinalTarget,
                0.5f);
            Vector3 diagonal = GroundMovement3D.MoveHorizontalVelocityToward(
                Vector3.Zero,
                diagonalTarget,
                0.5f);

            TestAssert.NearlyEqual(cardinal.Length(), diagonal.Length(), Tolerance,
                "Diagonal acceleration magnitude differs from cardinal acceleration.");
            TestAssert.NearlyEqual(0.5, diagonal.Length(), Tolerance,
                "Vector acceleration exceeded its configured per-frame change.");
        });

        context.Run("aim-ray-projects-onto-horizontal-xz-plane", () =>
        {
            Vector3 origin = new(2.0f, 10.0f, 3.0f);
            Vector3 rayDirection = new Vector3(0.2f, -1.0f, -0.4f).Normalized();

            TestAssert.True(
                AimPlaneProjection3D.TryIntersectHorizontalPlane(
                    origin,
                    rayDirection,
                    0.0f,
                    out Vector3 aimPoint),
                "Valid camera ray did not intersect the XZ aim plane.");
            TestAssert.NearlyEqual(0.0, aimPoint.Y, Tolerance,
                "Aim intersection escaped the configured horizontal plane.");
            TestAssert.True(
                AimPlaneProjection3D.TryGetHorizontalDirection(
                    Vector3.Zero,
                    aimPoint,
                    out Vector3 direction),
                "Projected aim point did not produce a horizontal direction.");
            TestAssert.NearlyEqual(0.0, direction.Y, Tolerance,
                "Aim direction included vertical tilt.");
        });

        context.Run("near-zero-aim-never-produces-invalid-rotation", () =>
        {
            Vector3 position = new(4.0f, 0.0f, -2.0f);
            TestAssert.False(
                AimPlaneProjection3D.TryGetYaw(position, position, out float yaw),
                "Near-zero aim unexpectedly produced a rotation.");
            TestAssert.True(float.IsFinite(yaw),
                "Rejected near-zero aim returned a non-finite yaw.");
            TestAssert.False(
                AimPlaneProjection3D.TryIntersectHorizontalPlane(
                    Vector3.Up,
                    Vector3.Right,
                    0.0f,
                    out _),
                "Horizontal ray unexpectedly intersected the aim plane.");
        });

        await context.RunAsync("disabled-and-terminal-states-prevent-movement", async () =>
        {
            Vector3 disabledTarget = GroundMovement3D.CalculateTargetVelocity(
                Vector2.Up,
                Vector3.Forward,
                Vector3.Right,
                6.0f,
                movementEnabled: false);
            TestAssert.Equal(Vector3.Zero, disabledTarget,
                "Disabled movement calculation produced target velocity.");

            PlayerController3D player = context.InstantiateScene<PlayerController3D>(
                "res://scenes/3d/player/Player3D.tscn");
            await context.WaitProcessFramesAsync();

            player.Velocity = new Vector3(3.0f, -1.0f, 4.0f);
            player.SetGameplayInputEnabled(false);
            AssertHorizontalVelocityStopped(player,
                "Disabling 3D gameplay input did not stop horizontal movement.");

            player.SetGameplayInputEnabled(true);
            player.Velocity = new Vector3(-2.0f, -1.0f, 5.0f);
            player.SetTerminalState(true);
            TestAssert.False(player.CanAcceptGameplayInput,
                "Terminal 3D player still accepts gameplay input.");
            AssertHorizontalVelocityStopped(player,
                "Terminal 3D state did not stop horizontal movement.");
        });

        await context.RunAsync("player3d-scene-has-explicit-physical-contract", async () =>
        {
            PlayerController3D player = context.InstantiateScene<PlayerController3D>(
                "res://scenes/3d/player/Player3D.tscn");
            await context.WaitProcessFramesAsync();

            CollisionShape3D normalCollisionShape =
                player.GetNode<CollisionShape3D>("%NormalCollisionShape3D");
            CollisionShape3D crouchCollisionShape =
                player.GetNode<CollisionShape3D>("%CrouchCollisionShape3D");
            CollisionShape3D crawlCollisionShape =
                player.GetNode<CollisionShape3D>("%CrawlCollisionShape3D");
            TestAssert.True(normalCollisionShape.Shape is CapsuleShape3D,
                "Player3D does not use an explicit normal capsule shape.");
            TestAssert.True(crouchCollisionShape.Shape is CapsuleShape3D,
                "Player3D does not use an explicit crouch capsule shape.");
            TestAssert.True(crawlCollisionShape.Shape is CapsuleShape3D,
                "Player3D does not use an explicit crawl capsule shape.");
            TestAssert.False(normalCollisionShape.Disabled,
                "Normal movement collision is not initially active.");
            TestAssert.True(crouchCollisionShape.Disabled,
                "Crouch movement collision is initially active.");
            TestAssert.True(crawlCollisionShape.Disabled,
                "Crawl movement collision is initially active.");
            TestAssert.True(
                !ReferenceEquals(
                    normalCollisionShape.Shape,
                    crouchCollisionShape.Shape) &&
                !ReferenceEquals(
                    normalCollisionShape.Shape,
                    crawlCollisionShape.Shape) &&
                !ReferenceEquals(
                    crouchCollisionShape.Shape,
                    crawlCollisionShape.Shape),
                "Movement collision profiles share a mutable shape resource.");
            TestAssert.True(
                player.GetNodeOrNull<Node3D>("%VisualPivot3D") is not null,
                "Player3D has no separate visual pivot.");
            TestAssert.True(
                player.GetNodeOrNull<MeshInstance3D>(
                    "VisualPivot3D/PostureVisuals3D/ForwardMarker3D") is not null,
                "Player3D has no visible forward marker inside the posture visuals root.");
            TestAssert.True(
                player.GetNodeOrNull<PlayerAimController3D>(
                    "%PlayerAimController3D") is not null,
                "Player3D has no aim controller.");
            TestAssert.True(
                player.GetMovementSpeed(MovementMode.Crawl) <
                player.GetMovementSpeed(MovementMode.Crouch) &&
                player.GetMovementSpeed(MovementMode.Crouch) <
                player.GetMovementSpeed(MovementMode.Walk) &&
                player.GetMovementSpeed(MovementMode.Walk) <
                player.GetMovementSpeed(MovementMode.Sprint),
                "Player3D posture speeds are not ordered Crawl < Crouch < Walk < Sprint.");
        });

        await context.RunAsync("crawl-exit-uses-a-stable-shape-query", async () =>
        {
            PlayerController3D player = context.InstantiateScene<PlayerController3D>(
                "res://scenes/3d/player/Player3D.tscn");
            await context.WaitPhysicsFramesAsync(2);
            TestAssert.True(player.TrySetPosture(MovementMode.Crawl),
                "Player3D could not enter Crawl.");

            StaticBody3D ceiling = context.AddNode(new StaticBody3D
            {
                Name = "LowCeiling",
                CollisionLayer = CollisionLayers3D.World,
                CollisionMask = 0
            });
            CollisionShape3D ceilingShape = new()
            {
                Shape = new BoxShape3D { Size = new Vector3(4.0f, 0.2f, 4.0f) },
                Position = new Vector3(0.0f, 1.2f, 0.0f)
            };
            ceiling.AddChild(ceilingShape);
            await context.WaitPhysicsFramesAsync(2);

            TestAssert.False(
                player.TrySetPosture(MovementMode.Walk, notifyOnFailure: false),
                "Player3D exited Crawl while the normal shape was blocked.");
            TestAssert.Equal(MovementMode.Crawl, player.CurrentPosture,
                "Blocked Crawl exit changed the authoritative posture.");

            ceiling.GlobalPosition = new Vector3(0.0f, 4.0f, 0.0f);
            await context.WaitPhysicsFramesAsync(2);
            TestAssert.True(
                player.TrySetPosture(MovementMode.Walk, notifyOnFailure: false),
                "Player3D did not exit Crawl after clearance became available.");
        });

        await context.RunAsync("testlevel3d-authors-floor-walls-and-passage", async () =>
        {
            Node3D level = context.InstantiateScene<Node3D>(
                "res://scenes/3d/levels/TestLevel3D.tscn");
            await context.WaitProcessFramesAsync();

            AssertStaticCollision(level, "%Floor3D");
            AssertStaticCollision(level, "%WallWest3D");
            AssertStaticCollision(level, "%WallEast3D");
            AssertStaticCollision(level, "%WallNorth3D");
            AssertStaticCollision(level, "%WallSouth3D");
            AssertStaticCollision(level, "%ObstacleWest3D");
            AssertStaticCollision(level, "%ObstacleEast3D");
            AssertStaticCollision(level, "%PassageWallLeft3D");
            AssertStaticCollision(level, "%PassageWallRight3D");
            CameraOccluder3D occluder =
                level.GetNodeOrNull<CameraOccluder3D>("%CameraOccluderDemo3D")
                ?? throw new TestAssertionException(
                    "TestLevel3D has no camera occluder demonstration.");
            MeshInstance3D occluderMesh =
                occluder.GetNode<MeshInstance3D>("%OccluderMesh3D");
            Material? originalMaterial = occluderMesh.GetActiveMaterial(0);
            occluder.SetOccluded(true);
            occluder._Process(1.0);
            StandardMaterial3D fadedMaterial =
                occluderMesh.GetActiveMaterial(0) as StandardMaterial3D
                ?? throw new TestAssertionException(
                    "Camera occluder installed no Compatibility fade material.");
            TestAssert.True(
                occluder.IsOccluded && fadedMaterial.AlbedoColor.A < 1.0f,
                "Camera occluder did not fade its material alpha.");
            TestAssert.NearlyEqual(0.0, occluderMesh.Transparency, Tolerance,
                "Camera occluder used unsupported geometry transparency.");
            occluder.SetOccluded(false);
            occluder._Process(1.0);
            TestAssert.False(occluder.IsOccluded,
                "Camera occluder did not restore its visible state.");
            TestAssert.True(
                ReferenceEquals(originalMaterial, occluderMesh.GetActiveMaterial(0)),
                "Camera occluder did not restore its original material state.");
            TestAssert.True(level.GetNodeOrNull<WorldEnvironment>("WorldEnvironment") is not null,
                "TestLevel3D has no WorldEnvironment.");
            TestAssert.True(level.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") is not null,
                "TestLevel3D has no authored directional light.");
            TestAssert.True(level.GetNodeOrNull<Marker3D>("%PlayerSpawn3D") is not null,
                "TestLevel3D has no explicit player spawn.");
        });

        await context.RunAsync(
            "technical-level3d-authors-the-complete-loop-contract",
            async () =>
            {
                Node3D level = context.InstantiateScene<Node3D>(
                    "res://scenes/3d/levels/TestLevel3D.tscn");
                await context.WaitPhysicsFramesAsync(2);

                CameraOccluder3D crawlOverhead =
                    level.GetNode<CameraOccluder3D>("%CrawlOnlyOverhead3D");
                CollisionShape3D overheadCollision =
                    crawlOverhead.GetNode<CollisionShape3D>("CollisionShape3D");
                BoxShape3D overheadShape = overheadCollision.Shape as BoxShape3D
                    ?? throw new TestAssertionException(
                        "Crawl-only overhead is not a box.");
                PlayerController3D detachedPlayer =
                    InstantiateDetached<PlayerController3D>(
                        "res://scenes/3d/player/Player3D.tscn");
                CollisionShape3D normalCollision =
                    detachedPlayer.GetNode<CollisionShape3D>(
                        "%NormalCollisionShape3D");
                CollisionShape3D crouchCollision =
                    detachedPlayer.GetNode<CollisionShape3D>(
                        "%CrouchCollisionShape3D");
                CollisionShape3D crawlCollision =
                    detachedPlayer.GetNode<CollisionShape3D>(
                        "%CrawlCollisionShape3D");
                CapsuleShape3D normalShape = normalCollision.Shape as CapsuleShape3D
                    ?? throw new TestAssertionException(
                        "Normal Player3D collision is not a capsule.");
                CapsuleShape3D crouchShape =
                    crouchCollision.Shape as CapsuleShape3D
                    ?? throw new TestAssertionException(
                        "Crouch Player3D collision is not a capsule.");
                CapsuleShape3D crawlShape = crawlCollision.Shape as CapsuleShape3D
                    ?? throw new TestAssertionException(
                        "Crawl Player3D collision is not a capsule.");
                float overheadBottom =
                    crawlOverhead.Position.Y - (overheadShape.Size.Y * 0.5f);
                float normalTop = normalCollision.Position.Y +
                                  (normalShape.Height * 0.5f);
                float crouchTop = crouchCollision.Position.Y +
                                  (crouchShape.Height * 0.5f);
                float crawlTop = crawlCollision.Position.Y +
                                 (crawlShape.Height * 0.5f);
                TestAssert.True(overheadBottom < normalTop,
                    "Normal Player3D profile fits under the crawl-only overhead.");
                TestAssert.True(overheadBottom < crouchTop,
                    "Crouch Player3D profile fits under the crawl-only overhead.");
                TestAssert.True(overheadBottom > crawlTop,
                    "Crawl Player3D profile cannot fit under its authored overhead.");
                detachedPlayer.Free();

                NavigationRegion3D navigationRegion =
                    level.GetNode<NavigationRegion3D>("%NavigationRegion3D");
                TestAssert.True(
                    navigationRegion.NavigationMesh is { } navigationMesh &&
                    navigationMesh.GetPolygonCount() > 0,
                    "TechnicalLevel3D has no authored navigation polygons.");
                TestAssert.True(
                    level.GetNodeOrNull<MutantController3D>(
                        "%TunnelMutant3D") is not null,
                    "TechnicalLevel3D has no 3D mutant patrol actor.");
                TestAssert.True(
                    level.GetNodeOrNull<DamageZone3D>("%DamageZone3D") is not null,
                    "TechnicalLevel3D has no hazard.");
                TestAssert.True(
                    level.GetNodeOrNull<LightExposureZone3D>("%DarkZone3D") is not null &&
                    level.GetNodeOrNull<LightExposureZone3D>("%BrightZone3D") is not null,
                    "TechnicalLevel3D does not author dark and bright gameplay zones.");
                TestAssert.True(
                    level.GetNodeOrNull<WorldItemPickup3D>(
                        "%ReplacementFusePickup3D") is not null &&
                    level.GetNodeOrNull<LootContainer3D>(
                        "%EmergencyCabinet3D") is not null,
                    "TechnicalLevel3D lacks its fuse pickup or loot container.");
                TestAssert.True(
                    level.GetNodeOrNull<PowerController3D>(
                        "%PowerController3D") is not null &&
                    level.GetNodeOrNull<FuseBox3D>(
                        "%MaintenanceFuseBox3D") is not null &&
                    level.GetNodeOrNull<PowerControlledLight3D>(
                        "%ExitBayPoweredLight3D") is not null,
                    "TechnicalLevel3D lacks its connected power room adapters.");
                EmergencyDoor3D emergencyDoor =
                    level.GetNode<EmergencyDoor3D>("%EmergencyExitDoor3D");
                ObjectiveExitZone3D exitZone =
                    level.GetNode<ObjectiveExitZone3D>("%ObjectiveExitZone3D");
                TestAssert.Equal(CollisionLayers3D.World,
                    emergencyDoor.GetNode<AnimatableBody3D>(
                        "%DoorPanel3D").CollisionLayer,
                    "Emergency door movement collision is not world geometry.");
                TestAssert.Equal(CollisionLayers3D.PlayerObjectiveSensor,
                    exitZone.CollisionMask,
                    "Technical exit detects something other than the objective sensor.");
            });

        await context.RunAsync("sprint-drains-and-then-recovers-stamina", async () =>
        {
            Main3D main = context.InstantiateScene<Main3D>(
                "res://scenes/3d/Main3D.tscn");
            await context.WaitPhysicsFramesAsync(8);
            double initialStamina = main.Player.Stamina.Current;

            Input.ActionPress("move_up");
            Input.ActionPress("sprint");
            try
            {
                await context.WaitPhysicsFramesAsync(30);
            }
            finally
            {
                Input.ActionRelease("sprint");
                Input.ActionRelease("move_up");
            }

            double drainedStamina = main.Player.Stamina.Current;
            TestAssert.True(drainedStamina < initialStamina,
                "Active 3D sprinting did not drain the shared stamina model.");
            await context.WaitPhysicsFramesAsync(90);
            TestAssert.True(main.Player.Stamina.Current > drainedStamina,
                "3D stamina did not recover after its configured delay.");
        });

        await context.RunAsync("main3d-composition-smoke-loads-and-honors-terminal-hooks", async () =>
        {
            Main3D main = context.InstantiateScene<Main3D>(
                "res://scenes/3d/Main3D.tscn");
            await context.WaitProcessFramesAsync(3);
            await context.WaitPhysicsFramesAsync(8);

            TestAssert.True(main.IsInitialized,
                "Main3D did not finish explicit composition-root initialization.");
            TestAssert.True(
                main.GetNodeOrNull<PlayerController3D>("%Player3D") is not null,
                "Main3D does not contain Player3D.");
            TopDownCamera3D camera =
                main.GetNodeOrNull<TopDownCamera3D>("%TopDownCamera3D")
                ?? throw new TestAssertionException(
                    "Main3D does not contain the dedicated top-down camera.");
            TestAssert.True(camera.Current,
                "Main3D top-down camera is not active.");
            TestAssert.Equal(Camera3D.ProjectionType.Orthogonal, camera.Projection,
                "Main3D camera is not orthographic.");
            Vector3 fixedRotation = camera.GlobalRotation;
            TestAssert.True(
                main.GetNodeOrNull<Node3D>("%TestLevel3D") is not null,
                "Main3D does not contain TestLevel3D.");
            TestAssert.True(main.Player.IsOnFloor(),
                "Player3D did not settle onto the authored physical floor.");

            main.Player.GlobalPosition = new Vector3(16.0f, 0.05f, 7.0f);
            main.Player.Velocity = Vector3.Zero;
            await context.WaitPhysicsFramesAsync(2);
            Input.ActionPress("move_right");
            try
            {
                await context.WaitPhysicsFramesAsync(45);
            }
            finally
            {
                Input.ActionRelease("move_right");
            }

            TestAssert.True(main.Player.GlobalPosition.X > 16.1f,
                "Player3D did not respond to the existing move_right action.");
            TestAssert.True(main.Player.GlobalPosition.X <= 17.46f,
                "Player3D passed through the authored east wall collision.");
            AssertVectorNearlyEqual(fixedRotation, camera.GlobalRotation,
                "Following the player changed the fixed isometric camera rotation.");

            main.Player.Velocity = new Vector3(3.0f, 0.0f, 4.0f);
            main.SetPlayerDead(true);
            AssertHorizontalVelocityStopped(main.Player,
                "Death hook did not stop the 3D player.");

            main.SetPlayerDead(false);
            TestAssert.True(main.Player.IsTerminalState,
                "Clearing a death flag restored gameplay after a terminal state.");
            main.Player.Velocity = new Vector3(-4.0f, 0.0f, 2.0f);
            main.SetPrototypeCompleted(true);
            AssertHorizontalVelocityStopped(main.Player,
                "Completion hook did not stop the 3D player.");
        });
    }

    private static void AssertStaticCollision(Node root, string nodePath)
    {
        StaticBody3D body = root.GetNodeOrNull<StaticBody3D>(nodePath)
            ?? throw new TestAssertionException(
                $"Expected StaticBody3D '{nodePath}' was not found.");
        CollisionShape3D shape = body.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")
            ?? throw new TestAssertionException(
                $"StaticBody3D '{nodePath}' has no CollisionShape3D.");
        TestAssert.True(shape.Shape is BoxShape3D,
            $"StaticBody3D '{nodePath}' does not use an authored box shape.");
    }

    private static TNode InstantiateDetached<TNode>(string resourcePath)
        where TNode : Node
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(resourcePath)
            ?? throw new TestAssertionException(
                $"Could not load required scene '{resourcePath}'.");
        return scene.Instantiate<TNode>();
    }

    private static void AssertHorizontalVelocityStopped(
        PlayerController3D player,
        string message)
    {
        TestAssert.NearlyEqual(0.0, player.Velocity.X, Tolerance, message);
        TestAssert.NearlyEqual(0.0, player.Velocity.Z, Tolerance, message);
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
