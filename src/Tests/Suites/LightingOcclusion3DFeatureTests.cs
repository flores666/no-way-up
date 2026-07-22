using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Tests.Framework;
using LineZero.World3D;
using LineZero.World3D.Perception;

namespace LineZero.Tests.Suites;

public sealed class LightingOcclusion3DFeatureTests : IFeatureTestSuite
{
    private const double Tolerance = 0.0001;
    private const float ReferenceViewportWidth = 1280.0f;
    private const float ReferenceViewportHeight = 720.0f;
    private const float TestLevelGroundHeight = 0.0f;
    private const float MaximumRelevantCasterHeight = 7.25f;
    private const float DirectionalShadowSafetyMargin = 4.0f;

    public string Id => "lighting-occlusion-3d";

    public string Description =>
        "GL Compatibility fades, silhouette occlusion, authored blockers, and light isolation";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync(
            "compatibility-fade-uses-local-material-alpha-and-restores-exact-state",
            async () =>
            {
                StandardMaterial3D sharedMaterial = new()
                {
                    AlbedoColor = new Color(0.32f, 0.41f, 0.48f, 0.82f),
                    Roughness = 0.63f,
                    Transparency = BaseMaterial3D.TransparencyEnum.Disabled
                };
                CameraOccluder3D occluder = CreateOccluder(
                    "MaterialFadeOccluder3D",
                    Vector3.Zero,
                    sharedMaterial,
                    out MeshInstance3D firstMesh,
                    out CollisionShape3D collision,
                    includeSecondVisual: true,
                    out MeshInstance3D? secondMesh);
                firstMesh.CastShadow =
                    GeometryInstance3D.ShadowCastingSetting.DoubleSided;
                context.AddNode(occluder);
                await context.WaitProcessFramesAsync(2);

                Color originalAlbedo = sharedMaterial.AlbedoColor;
                BaseMaterial3D.TransparencyEnum originalTransparency =
                    sharedMaterial.Transparency;
                Material? originalFirstOverride =
                    firstMesh.GetSurfaceOverrideMaterial(0);
                Material? originalSecondOverride =
                    secondMesh?.GetSurfaceOverrideMaterial(0);
                TestAssert.Equal(2, occluder.ConfiguredShadowProxyCount,
                    "Every originally shadow-casting occluder visual requires one shadow proxy.");
                MeshInstance3D firstShadowProxy = GetShadowProxy(firstMesh);
                MeshInstance3D secondShadowProxy = GetShadowProxy(
                    secondMesh ?? throw new TestAssertionException(
                        "Additional occluder visual is missing."));

                occluder.SetOccluded(true);
                TestAssert.Equal(2, occluder.ActiveShadowProxyCount,
                    "Shadow ownership was not transferred atomically when fading began.");
                occluder._Process(1.0);

                StandardMaterial3D fadedFirst =
                    firstMesh.GetActiveMaterial(0) as StandardMaterial3D
                    ?? throw new TestAssertionException(
                        "Occluder fade did not install a StandardMaterial3D override.");
                StandardMaterial3D fadedSecond =
                    secondMesh?.GetActiveMaterial(0) as StandardMaterial3D
                    ?? throw new TestAssertionException(
                        "Additional configured visual did not receive a fade material.");
                TestAssert.False(ReferenceEquals(sharedMaterial, fadedFirst),
                    "Occluder reused the shared source material as mutable fade state.");
                TestAssert.False(ReferenceEquals(sharedMaterial, fadedSecond),
                    "Additional visual reused the shared source material.");
                TestAssert.Equal(BaseMaterial3D.TransparencyEnum.Alpha,
                    fadedFirst.Transparency,
                    "Occluder fade is not GL Compatibility alpha transparency.");
                TestAssert.True(fadedFirst.AlbedoColor.A < originalAlbedo.A,
                    "Occluder material alpha did not decrease.");
                TestAssert.NearlyEqual(0.0, firstMesh.Transparency, Tolerance,
                    "Compatibility fade still uses GeometryInstance3D.Transparency.");
                TestAssert.Equal(originalAlbedo, sharedMaterial.AlbedoColor,
                    "Fade mutated the shared source material color or alpha.");
                TestAssert.Equal(originalTransparency, sharedMaterial.Transparency,
                    "Fade mutated the shared source material transparency mode.");
                TestAssert.Equal(GeometryInstance3D.ShadowCastingSetting.Off,
                    firstMesh.CastShadow,
                    "Transparent camera visual still owns shadow casting and can flicker.");
                AssertActiveShadowProxy(
                    firstMesh,
                    firstShadowProxy,
                    sharedMaterial,
                    "Primary faded visual");
                AssertActiveShadowProxy(
                    secondMesh ?? throw new TestAssertionException(
                        "Additional occluder visual is missing."),
                    secondShadowProxy,
                    sharedMaterial,
                    "Additional faded visual");
                TestAssert.False(collision.Disabled,
                    "Fading disabled the occluder's physical collision.");

                occluder.SetOccluded(false);
                occluder._Process(1.0);
                TestAssert.True(
                    ReferenceEquals(
                        originalFirstOverride,
                        firstMesh.GetSurfaceOverrideMaterial(0)),
                    "First visual did not restore its exact surface override.");
                TestAssert.True(
                    ReferenceEquals(
                        originalSecondOverride,
                        secondMesh?.GetSurfaceOverrideMaterial(0)),
                    "Additional visual did not restore its exact surface override.");
                TestAssert.Equal(
                    GeometryInstance3D.ShadowCastingSetting.DoubleSided,
                    firstMesh.CastShadow,
                    "Occluder did not restore its original shadow mode.");
                TestAssert.Equal(0, occluder.ActiveShadowProxyCount,
                    "Restored occluder kept a duplicate shadow-only proxy active.");
                TestAssert.False(firstShadowProxy.Visible || secondShadowProxy.Visible,
                    "Restored occluder still renders a shadow proxy.");
                TestAssert.Equal(originalAlbedo, sharedMaterial.AlbedoColor,
                    "Restore changed the shared source material.");
            });

        await context.RunAsync(
            "multiple-occluders-use-clear-query-hysteresis-and-keep-collision",
            async () =>
            {
                Node3D root = context.AddNode(new Node3D
                {
                    Name = "MultipleOcclusionFixture3D"
                });
                CharacterBody3D target = CreateTargetBody();
                Camera3D camera = new()
                {
                    Name = "Camera3D",
                    Position = new Vector3(0.0f, 4.0f, 8.0f)
                };
                CameraOcclusionController3D controller = new()
                {
                    Name = "CameraOcclusionController3D"
                };
                CameraOccluder3D first = CreateOccluder(
                    "FirstOccluder3D",
                    new Vector3(0.0f, 2.5f, 5.0f),
                    CreateMaterial(),
                    out _,
                    out CollisionShape3D firstCollision,
                    includeSecondVisual: false,
                    out _);
                CameraOccluder3D second = CreateOccluder(
                    "SecondOccluder3D",
                    new Vector3(0.0f, 2.0f, 3.0f),
                    CreateMaterial(),
                    out _,
                    out CollisionShape3D secondCollision,
                    includeSecondVisual: false,
                    out _);
                root.AddChild(target);
                root.AddChild(camera);
                root.AddChild(controller);
                root.AddChild(first);
                root.AddChild(second);
                await context.WaitPhysicsFramesAsync(2);

                controller.Bind(camera, target);
                controller.SetPhysicsProcess(false);
                controller.RefreshOcclusion();
                TestAssert.True(first.IsOccluded && second.IsOccluded,
                    "Simultaneous occluders were not both detected.");
                TestAssert.Equal(1, first.ActiveShadowProxyCount,
                    "First faded occluder lost its shadow proxy.");
                TestAssert.Equal(1, second.ActiveShadowProxyCount,
                    "Second faded occluder lost its shadow proxy.");
                TestAssert.Equal(2, controller.FadedOccluderCount,
                    "Faded HUD count does not match simultaneous occluders.");
                TestAssert.False(firstCollision.Disabled || secondCollision.Disabled,
                    "Detected occluder collision was disabled.");

                first.Position += Vector3.Right * 6.0f;
                second.Position += Vector3.Right * 6.0f;
                await context.WaitPhysicsFramesAsync(2);
                controller.RefreshOcclusion();
                TestAssert.True(first.IsOccluded && second.IsOccluded,
                    "One clear edge query restored occluders and can flicker.");
                controller.RefreshOcclusion();
                TestAssert.False(first.IsOccluded || second.IsOccluded,
                    "Occluders did not restore after consecutive clear queries.");
                first._Process(1.0);
                second._Process(1.0);
                controller.RefreshOcclusion();
                TestAssert.Equal(0, first.ActiveShadowProxyCount,
                    "First restored occluder kept duplicate shadow casting active.");
                TestAssert.Equal(0, second.ActiveShadowProxyCount,
                    "Second restored occluder kept duplicate shadow casting active.");
                TestAssert.Equal(0, controller.FadedOccluderCount,
                    "Faded HUD count remained after both materials restored.");
            });


        await context.RunAsync(
            "shadow-proxy-preserves-material-override-without-sharing-fade-state",
            async () =>
            {
                StandardMaterial3D meshMaterial = CreateMaterial();
                StandardMaterial3D overrideMaterial = new()
                {
                    AlbedoColor = new Color(0.48f, 0.31f, 0.22f, 1.0f),
                    Roughness = 0.57f
                };
                StandardMaterial3D overlayMaterial = new()
                {
                    AlbedoColor = new Color(0.16f, 0.28f, 0.44f, 0.65f),
                    Roughness = 0.72f
                };
                CameraOccluder3D occluder = CreateOccluder(
                    "MaterialOverrideOccluder3D",
                    Vector3.Zero,
                    meshMaterial,
                    out MeshInstance3D sourceMesh,
                    out _,
                    includeSecondVisual: false,
                    out _);
                sourceMesh.MaterialOverride = overrideMaterial;
                sourceMesh.MaterialOverlay = overlayMaterial;
                context.AddNode(occluder);
                await context.WaitProcessFramesAsync(2);

                MeshInstance3D shadowProxy = GetShadowProxy(sourceMesh);
                TestAssert.True(
                    ReferenceEquals(overrideMaterial, shadowProxy.MaterialOverride),
                    "Shadow proxy did not retain the exact authored material override.");
                TestAssert.True(
                    ReferenceEquals(overlayMaterial, shadowProxy.MaterialOverlay),
                    "Shadow proxy did not retain the exact authored material overlay.");
                occluder.SetOccluded(true);
                occluder._Process(1.0);
                TestAssert.False(
                    ReferenceEquals(overrideMaterial, sourceMesh.MaterialOverride),
                    "Camera fade mutated the authored material override in place.");
                TestAssert.False(
                    ReferenceEquals(overlayMaterial, sourceMesh.MaterialOverlay),
                    "Camera fade left an authored material overlay opaque.");
                TestAssert.True(
                    ReferenceEquals(overrideMaterial, shadowProxy.MaterialOverride),
                    "Camera fade replaced the shadow proxy's opaque material.");
                TestAssert.True(
                    ReferenceEquals(overlayMaterial, shadowProxy.MaterialOverlay),
                    "Camera fade replaced the shadow proxy's authored overlay.");

                occluder.SetOccluded(false);
                occluder._Process(1.0);
                TestAssert.True(
                    ReferenceEquals(overrideMaterial, sourceMesh.MaterialOverride),
                    "Restore did not reinstate the exact authored material override.");
                TestAssert.True(
                    ReferenceEquals(overlayMaterial, sourceMesh.MaterialOverlay),
                    "Restore did not reinstate the exact authored material overlay.");
                TestAssert.False(shadowProxy.Visible,
                    "Restored material-override occluder kept its shadow proxy active.");
            });

        await context.RunAsync(
            "shadow-proxies-survive-scene-reentry-without-duplication",
            async () =>
            {
                Node3D root = context.AddNode(new Node3D
                {
                    Name = "ShadowProxyReentryFixture3D"
                });
                CameraOccluder3D occluder = CreateOccluder(
                    "ReenteredOccluder3D",
                    Vector3.Zero,
                    CreateMaterial(),
                    out MeshInstance3D sourceMesh,
                    out _,
                    includeSecondVisual: false,
                    out _);
                root.AddChild(occluder);
                await context.WaitProcessFramesAsync(2);
                TestAssert.Equal(1, CountShadowProxyChildren(sourceMesh),
                    "Initial scene entry created an invalid shadow proxy count.");
                occluder.SetOccluded(true);
                TestAssert.Equal(1, occluder.ActiveShadowProxyCount,
                    "Initial occluder could not transfer shadow ownership.");

                root.RemoveChild(occluder);
                await context.WaitProcessFramesAsync(1);
                TestAssert.False(occluder.IsOccluded,
                    "Exiting the scene tree retained stale logical occlusion state.");
                TestAssert.Equal(0, occluder.ActiveShadowProxyCount,
                    "Exiting the scene tree left a generated shadow active.");
                occluder.RequestReady();
                root.AddChild(occluder);
                await context.WaitProcessFramesAsync(2);

                TestAssert.Equal(1, occluder.ConfiguredShadowProxyCount,
                    "Re-entered occluder lost its configured shadow proxy.");
                TestAssert.Equal(1, CountShadowProxyChildren(sourceMesh),
                    "Repeated scene entry duplicated generated shadow geometry.");
                occluder.SetOccluded(true);
                TestAssert.Equal(1, occluder.ActiveShadowProxyCount,
                    "Re-entered occluder could not transfer shadow ownership.");
            });


        await context.RunAsync(
            "shadow-disabled-visuals-never-gain-a-generated-shadow",
            async () =>
            {
                CameraOccluder3D occluder = CreateOccluder(
                    "NoShadowOccluder3D",
                    Vector3.Zero,
                    CreateMaterial(),
                    out MeshInstance3D sourceMesh,
                    out _,
                    includeSecondVisual: false,
                    out _);
                sourceMesh.CastShadow =
                    GeometryInstance3D.ShadowCastingSetting.Off;
                context.AddNode(occluder);
                await context.WaitProcessFramesAsync(2);

                TestAssert.Equal(0, occluder.ConfiguredShadowProxyCount,
                    "An originally shadow-disabled visual received a shadow proxy.");
                occluder.SetOccluded(true);
                occluder._Process(1.0);
                TestAssert.Equal(0, occluder.ActiveShadowProxyCount,
                    "Fading enabled a shadow that did not exist in the authored scene.");
                TestAssert.Equal(
                    GeometryInstance3D.ShadowCastingSetting.Off,
                    sourceMesh.CastShadow,
                    "Fading changed an authored shadow-disabled visual.");
                occluder.SetOccluded(false);
                occluder._Process(1.0);
                TestAssert.Equal(
                    GeometryInstance3D.ShadowCastingSetting.Off,
                    sourceMesh.CastShadow,
                    "Restore enabled a shadow that was originally disabled.");
            });

        await context.RunAsync(
            "silhouette-edge-detects-blocker-missed-by-centre-ray",
            async () =>
            {
                Node3D root = context.AddNode(new Node3D
                {
                    Name = "SilhouetteOcclusionFixture3D"
                });
                CharacterBody3D target = CreateTargetBody();
                Camera3D camera = new()
                {
                    Name = "Camera3D",
                    Position = new Vector3(0.0f, 4.0f, 8.0f)
                };
                CameraOcclusionController3D controller = new()
                {
                    Name = "CameraOcclusionController3D"
                };
                CameraOccluder3D edgeOccluder = CreateOccluder(
                    "EdgeOccluder3D",
                    new Vector3(0.29f, 2.45f, 4.0f),
                    CreateMaterial(),
                    out _,
                    out _,
                    includeSecondVisual: false,
                    out _,
                    size: new Vector3(0.18f, 3.0f, 0.3f));
                root.AddChild(target);
                root.AddChild(camera);
                root.AddChild(controller);
                root.AddChild(edgeOccluder);
                await context.WaitPhysicsFramesAsync(2);

                Godot.Collections.Array<Rid> exclusions = new() { target.GetRid() };
                PhysicsRayQueryParameters3D centreQuery =
                    PhysicsRayQueryParameters3D.Create(
                        camera.GlobalPosition,
                        target.GlobalPosition + (Vector3.Up * 0.9f),
                        CollisionLayers3D.CameraOccluder,
                        exclusions);
                centreQuery.CollideWithAreas = false;
                centreQuery.CollideWithBodies = true;
                Godot.Collections.Dictionary centreHit =
                    target.GetWorld3D().DirectSpaceState.IntersectRay(centreQuery);
                TestAssert.Equal(0, centreHit.Count,
                    "Silhouette fixture accidentally blocks the old centre ray.");

                controller.Bind(camera, target);
                controller.SetPhysicsProcess(false);
                controller.RefreshOcclusion();
                TestAssert.True(edgeOccluder.IsOccluded,
                    "Horizontal silhouette obstruction was not detected.");
            });

        await context.RunAsync(
            "test-level-configures-all-intended-solid-blockers-only",
            async () =>
            {
                Node3D level = context.InstantiateScene<Node3D>(
                    "res://scenes/3d/levels/TestLevel3D.tscn");
                await context.WaitProcessFramesAsync(2);
                string[] expectedOccluders =
                {
                    "%WallEast3D",
                    "%WallSouth3D",
                    "%ObstacleWest3D",
                    "%ObstacleEast3D",
                    "%PassageWallLeft3D",
                    "%PassageWallRight3D",
                    "%CrawlOnlyOverhead3D",
                    "%LowCeiling3D",
                    "%ExitPartitionWest3D",
                    "%ExitPartitionEast3D"
                };
                for (int index = 0; index < expectedOccluders.Length; index++)
                {
                    CameraOccluder3D occluder =
                        level.GetNodeOrNull<CameraOccluder3D>(
                            expectedOccluders[index])
                        ?? throw new TestAssertionException(
                            $"Expected blocker '{expectedOccluders[index]}' is not an occluder.");
                    TestAssert.True(
                        (occluder.CollisionLayer & CollisionLayers3D.World) != 0 &&
                        (occluder.CollisionLayer &
                         CollisionLayers3D.CameraOccluder) != 0,
                        $"'{expectedOccluders[index]}' lost World or occluder collision.");
                    CollisionShape3D collision =
                        occluder.GetNode<CollisionShape3D>("CollisionShape3D");
                    TestAssert.False(collision.Disabled,
                        $"'{expectedOccluders[index]}' has disabled collision.");
                }

                StaticBody3D floor = level.GetNode<StaticBody3D>("%Floor3D");
                TestAssert.False(floor is CameraOccluder3D,
                    "Floor was incorrectly configured as a camera occluder.");
                TestAssert.Equal(0u,
                    floor.CollisionLayer & CollisionLayers3D.CameraOccluder,
                    "Floor participates in camera occlusion queries.");
                Area3D darkZone = level.GetNode<Area3D>("%DarkZone3D");
                TestAssert.Equal(0u,
                    darkZone.CollisionLayer & CollisionLayers3D.CameraOccluder,
                    "Gameplay visibility area participates in camera occlusion.");
            });

        await context.RunAsync(
            "supplied-locations-have-no-unfaded-silhouette-obstruction",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitPhysicsFramesAsync(5);
                TopDownCamera3D camera = main.GetNode<TopDownCamera3D>(
                    "%TopDownCamera3D");
                CameraOcclusionController3D controller =
                    main.GetNode<CameraOcclusionController3D>(
                        "%CameraOcclusionController3D");
                controller.SetPhysicsProcess(false);
                main.Player.SetPhysicsProcess(false);
                camera.SmoothingEnabled = false;

                await AssertLocationOccludesAsync(
                    context,
                    main,
                    camera,
                    controller,
                    new Vector3(4.7f, 0.05f, 7.1f),
                    "%ObstacleEast3D");
                await AssertLocationOccludesAsync(
                    context,
                    main,
                    camera,
                    controller,
                    new Vector3(7.9f, 0.05f, 8.6f),
                    "%ObstacleEast3D");
                await AssertLocationOccludesAsync(
                    context,
                    main,
                    camera,
                    controller,
                    new Vector3(0.0f, 0.05f, -7.0f),
                    "%CrawlOnlyOverhead3D");
                await AssertLocationOccludesAsync(
                    context,
                    main,
                    camera,
                    controller,
                    new Vector3(-10.0f, 0.05f, 8.0f),
                    "%LowCeiling3D");
            });

        await context.RunAsync(
            "removed-active-occluder-is-cleaned-safely",
            async () =>
            {
                Node3D root = context.AddNode(new Node3D
                {
                    Name = "RemovedOccluderFixture3D"
                });
                CharacterBody3D target = CreateTargetBody();
                Camera3D camera = new()
                {
                    Position = new Vector3(0.0f, 4.0f, 8.0f)
                };
                CameraOcclusionController3D controller = new();
                CameraOccluder3D occluder = CreateOccluder(
                    "RemovedOccluder3D",
                    new Vector3(0.0f, 2.5f, 4.0f),
                    CreateMaterial(),
                    out _,
                    out _,
                    includeSecondVisual: false,
                    out _);
                root.AddChild(target);
                root.AddChild(camera);
                root.AddChild(controller);
                root.AddChild(occluder);
                await context.WaitPhysicsFramesAsync(2);
                controller.Bind(camera, target);
                controller.SetPhysicsProcess(false);
                controller.RefreshOcclusion();
                TestAssert.Equal(1, controller.FadedOccluderCount,
                    "Removal fixture did not activate its occluder.");

                occluder.QueueFree();
                await context.WaitProcessFramesAsync(2);
                controller.RefreshOcclusion();
                controller.RefreshOcclusion();
                TestAssert.Equal(0, controller.FadedOccluderCount,
                    "Freed occluder remained in active fade state.");
            });

        await context.RunAsync(
            "zone-marker-is-hidden-and-development-visibility-is-explicit",
            async () =>
            {
                LightExposureZone3D zone =
                    context.InstantiateScene<LightExposureZone3D>(
                        "res://scenes/3d/levels/LightExposureZone3D.tscn");
                await context.WaitProcessFramesAsync(2);
                MeshInstance3D marker = zone.GetNode<MeshInstance3D>(
                    "ZoneMarker3D");
                float multiplier = zone.VisibilityMultiplier;
                uint collisionLayer = zone.CollisionLayer;
                uint collisionMask = zone.CollisionMask;
                TestAssert.False(marker.Visible,
                    "Light-exposure debug marker is visible by default.");

                PropertyInfo? debugProperty =
                    typeof(LightExposureZone3D).GetProperty(
                        "ShowDevelopmentMarker");
                TestAssert.True(debugProperty is not null,
                    "Light zone exposes no explicit development marker flag.");
                debugProperty?.SetValue(zone, true);
                TestAssert.True(marker.Visible,
                    "Development flag did not reveal the zone marker.");
                debugProperty?.SetValue(zone, false);
                TestAssert.False(marker.Visible,
                    "Development flag did not hide the zone marker again.");
                TestAssert.NearlyEqual(multiplier, zone.VisibilityMultiplier,
                    Tolerance,
                    "Marker visibility changed deterministic visibility.");
                TestAssert.Equal(collisionLayer, zone.CollisionLayer,
                    "Marker visibility changed the zone collision layer.");
                TestAssert.Equal(collisionMask, zone.CollisionMask,
                    "Marker visibility changed the zone collision mask.");
            });

        await context.RunAsync(
            "flashlight-excludes-player-visuals-but-still-lights-world-walls",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(2);
                SpotLight3D flashlight = main.Player.GetNode<SpotLight3D>(
                    "%FlashlightSpotLight3D");
                Node3D flashlightController = main.Player.GetNode<Node3D>(
                    "%PlayerFlashlightController3D");
                Node3D visualPivot = main.Player.GetNode<Node3D>("%VisualPivot3D");
                foreach (MeshInstance3D visual in EnumerateMeshDescendants(visualPivot))
                {
                    TestAssert.Equal(0u, visual.Layers & flashlight.LightCullMask,
                        $"Player visual '{visual.Name}' can self-shadow the flashlight.");
                }

                MeshInstance3D aimMarker = main.GetNode<MeshInstance3D>(
                    "%AimPointMarker3D");
                TestAssert.Equal(RenderLayers3D.World, flashlight.LightCullMask,
                    "Flashlight uses an implicit or unrelated render-layer mask.");
                TestAssert.True(flashlight.ShadowEnabled,
                    "Flashlight no longer stops at solid world walls.");
                TestAssert.True(
                    flashlight.ShadowOpacity > 0.0f &&
                    flashlight.ShadowOpacity < 0.8f,
                    "Flashlight retained an excessive full-strength shadow.");
                TestAssert.Equal(0u,
                    aimMarker.Layers & flashlight.LightCullMask,
                    "Aim marker participates in flashlight lighting.");
                TestAssert.True(flashlightController.Position.Z < -0.6f,
                    "Flashlight origin is not clearly in front of the player body.");
                TestAssert.True(flashlightController.RotationDegrees.X < -20.0f,
                    "Flashlight does not point down toward the floor predictably.");

                Node3D level = main.GetNode<Node3D>("%TestLevel3D");
                CameraOccluder3D worldWall = level.GetNode<CameraOccluder3D>(
                    "%WallSouth3D");
                MeshInstance3D wallVisual = GetFirstMesh(worldWall);
                TestAssert.True(
                    (wallVisual.Layers & flashlight.LightCullMask) != 0,
                    "Flashlight cull mask no longer includes solid world walls.");
                TestAssert.False(
                    wallVisual.CastShadow ==
                    GeometryInstance3D.ShadowCastingSetting.Off,
                    "Visible solid wall no longer blocks flashlight illumination.");
            });

        await context.RunAsync(
            "environment-and-lights-use-readable-bounded-shadows",
            async () =>
            {
                Node3D level = context.InstantiateScene<Node3D>(
                    "res://scenes/3d/levels/TestLevel3D.tscn");
                Node3D poweredLightScene = context.InstantiateScene<Node3D>(
                    "res://scenes/3d/levels/PowerControlledLight3D.tscn");
                await context.WaitProcessFramesAsync(2);
                WorldEnvironment worldEnvironment =
                    level.GetNode<WorldEnvironment>("WorldEnvironment");
                Godot.Environment environment = worldEnvironment.Environment
                    ?? throw new TestAssertionException(
                        "TestLevel3D WorldEnvironment has no Environment resource.");
                DirectionalLight3D directional =
                    level.GetNode<DirectionalLight3D>("DirectionalLight3D");
                OmniLight3D bright = level.GetNode<OmniLight3D>(
                    "%BrightZone3D/BrightZoneLight3D");
                OmniLight3D powered = poweredLightScene.GetNode<OmniLight3D>(
                    "%PoweredLight3D");

                TestAssert.True(
                    environment.AmbientLightEnergy >= 0.45f &&
                    environment.AmbientLightEnergy <= 0.7f,
                    "Ambient fill is too black or too bright for readable shadows.");
                AssertReducedShadow(directional, "DirectionalLight3D");
                AssertReducedShadow(bright, "BrightZoneLight3D");
                AssertReducedShadow(powered, "PoweredLight3D");
                TestAssert.True(directional.LightEnergy < 1.1f,
                    "Directional light retained its excessive original energy.");
                TestAssert.True(bright.LightEnergy < 5.0f,
                    "Bright local light retained its excessive original energy.");
                TestAssert.True(powered.LightEnergy < 4.5f,
                    "Powered local light retained its excessive original energy.");
            });

        await context.RunAsync(
            "orthographic-camera-and-directional-shadow-cover-visible-ground",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                await context.WaitProcessFramesAsync(2);
                TopDownCamera3D camera = main.GetNode<TopDownCamera3D>(
                    "%TopDownCamera3D");
                Node3D level = main.GetNode<Node3D>("%TestLevel3D");
                DirectionalLight3D directional =
                    level.GetNode<DirectionalLight3D>("DirectionalLight3D");

                TestAssert.Equal(Camera3D.ProjectionType.Orthogonal,
                    camera.Projection,
                    "TopDownCamera3D no longer uses orthographic projection.");
                TestAssert.True(
                    float.IsFinite(camera.Near) &&
                    float.IsFinite(camera.Far) &&
                    camera.Near > 0.0f &&
                    camera.Far > camera.Near,
                    "TopDownCamera3D has invalid near/far clip distances.");
                TestAssert.True(camera.Far <= 64.0f,
                    "TopDownCamera3D far clip is larger than the technical level requires.");
                TestAssert.True(directional.ShadowEnabled,
                    "DirectionalLight3D shadows were disabled instead of stabilized.");
                TestAssert.Equal(0, (int)directional.DirectionalShadowMode,
                    "Fixed orthographic camera must use one orthogonal directional shadow map.");
                TestAssert.False(directional.DirectionalShadowBlendSplits,
                    "Orthogonal directional shadows should not retain split blending state.");
                TestAssert.NearlyEqual(1.0,
                    directional.DirectionalShadowFadeStart,
                    Tolerance,
                    "Directional shadows begin fading inside the configured coverage.");

                float pitchRadians = Mathf.DegToRad(camera.PitchDegrees);
                float lightElevationRadians = Mathf.DegToRad(
                    Mathf.Abs(directional.RotationDegrees.X));
                float focusHeightAboveGround =
                    main.Player.GlobalPosition.Y +
                    camera.TargetHeightOffset -
                    TestLevelGroundHeight;
                float centreGroundDepth =
                    camera.CameraDistance +
                    (focusHeightAboveGround / MathF.Sin(pitchRadians));
                float farVisibleGroundDepth =
                    centreGroundDepth +
                    ((camera.OrthographicSize * 0.5f) *
                     MathF.Cos(pitchRadians) /
                     MathF.Sin(pitchRadians));
                float maximumGroundShadowReach =
                    MaximumRelevantCasterHeight /
                    MathF.Tan(lightElevationRadians);
                float maximumCameraDepthReach =
                    maximumGroundShadowReach * MathF.Cos(pitchRadians);
                float requiredShadowDistance =
                    farVisibleGroundDepth +
                    maximumCameraDepthReach +
                    DirectionalShadowSafetyMargin;

                float aspectRatio =
                    ReferenceViewportWidth / ReferenceViewportHeight;
                float visibleGroundWidth =
                    camera.OrthographicSize * aspectRatio;
                float visibleGroundDepth =
                    camera.OrthographicSize / MathF.Sin(pitchRadians);
                TestAssert.True(
                    visibleGroundWidth > 39.0f &&
                    visibleGroundDepth > 26.0f,
                    "Reference orthographic ground footprint calculation is invalid.");
                TestAssert.True(
                    directional.DirectionalShadowMaxDistance >=
                    requiredShadowDistance,
                    $"Directional shadow distance {directional.DirectionalShadowMaxDistance:0.00} " +
                    $"does not cover required camera depth {requiredShadowDistance:0.00}.");
                TestAssert.True(
                    directional.DirectionalShadowMaxDistance <=
                    requiredShadowDistance + 6.0f,
                    "Directional shadow distance is excessively large and wastes resolution.");
                TestAssert.True(
                    camera.Far >=
                    directional.DirectionalShadowMaxDistance + 4.0f,
                    "Camera far clip does not retain a bounded margin beyond directional shadows.");
                TestAssert.True(
                    directional.DirectionalShadowPancakeSize >=
                    MaximumRelevantCasterHeight + 1.0f &&
                    directional.DirectionalShadowPancakeSize <= 12.0f,
                    "Directional shadow pancake does not cover level height with a bounded margin.");
            });

        await context.RunAsync(
            "positional-shadow-lights-use-real-ranges-without-camera-fade",
            async () =>
            {
                Main3D main = context.InstantiateScene<Main3D>(
                    "res://scenes/3d/Main3D.tscn");
                Node3D poweredLightScene = context.InstantiateScene<Node3D>(
                    "res://scenes/3d/levels/PowerControlledLight3D.tscn");
                await context.WaitProcessFramesAsync(2);

                SpotLight3D flashlight = main.Player.GetNode<SpotLight3D>(
                    "%FlashlightSpotLight3D");
                Node3D level = main.GetNode<Node3D>("%TestLevel3D");
                OmniLight3D bright = level.GetNode<OmniLight3D>(
                    "%BrightZone3D/BrightZoneLight3D");
                OmniLight3D powered = poweredLightScene.GetNode<OmniLight3D>(
                    "%PoweredLight3D");

                AssertPositionalShadowLight(
                    flashlight,
                    flashlight.SpotRange,
                    18.0f,
                    "FlashlightSpotLight3D");
                AssertPositionalShadowLight(
                    bright,
                    bright.OmniRange,
                    8.0f,
                    "BrightZoneLight3D");
                AssertPositionalShadowLight(
                    powered,
                    powered.OmniRange,
                    8.0f,
                    "PoweredLight3D");
            });

        await context.RunAsync(
            "important-world-casters-have-no-distance-visibility-culling",
            async () =>
            {
                Node3D level = context.InstantiateScene<Node3D>(
                    "res://scenes/3d/levels/TestLevel3D.tscn");
                await context.WaitProcessFramesAsync(2);
                string[] casterPaths =
                {
                    "%Floor3D",
                    "%ObstacleWest3D",
                    "%ObstacleEast3D",
                    "%PassageWallLeft3D",
                    "%PassageWallRight3D",
                    "%CrawlOnlyOverhead3D",
                    "%LowCeiling3D",
                    "%ExitPartitionWest3D",
                    "%ExitPartitionEast3D"
                };

                for (int index = 0; index < casterPaths.Length; index++)
                {
                    Node3D caster = level.GetNode<Node3D>(casterPaths[index]);
                    MeshInstance3D visual = GetFirstMesh(caster);
                    TestAssert.False(
                        visual.CastShadow ==
                        GeometryInstance3D.ShadowCastingSetting.Off,
                        $"Important caster '{casterPaths[index]}' has shadows disabled.");
                    TestAssert.NearlyEqual(0.0,
                        visual.VisibilityRangeBegin,
                        Tolerance,
                        $"Important caster '{casterPaths[index]}' has a visibility begin distance.");
                    TestAssert.NearlyEqual(0.0,
                        visual.VisibilityRangeEnd,
                        Tolerance,
                        $"Important caster '{casterPaths[index]}' has a visibility end distance.");
                    TestAssert.Equal(0,
                        (int)visual.VisibilityRangeFadeMode,
                        $"Important caster '{casterPaths[index]}' has visibility-range fading.");
                }
            });

        await context.RunAsync("main3d-and-legacy-2d-scenes-still-load", async () =>
        {
            Main3D main = context.InstantiateScene<Main3D>(
                "res://scenes/3d/Main3D.tscn");
            await context.WaitProcessFramesAsync(2);
            TestAssert.True(main.IsInitialized,
                "Main3D did not complete composition after presentation changes.");

            Node legacyMain = context.InstantiateScene<Node>(
                "res://scenes/main/Main.tscn");
            await context.WaitProcessFramesAsync(2);
            TestAssert.True(legacyMain.IsInsideTree(),
                "Legacy 2D scene no longer loads.");
        });
    }

    private static async Task AssertLocationOccludesAsync(
        FeatureTestContext context,
        Main3D main,
        TopDownCamera3D camera,
        CameraOcclusionController3D controller,
        Vector3 position,
        string expectedOccluderPath)
    {
        main.Player.GlobalPosition = position;
        main.Player.Velocity = Vector3.Zero;
        camera._Process(1.0 / 60.0);
        await context.WaitPhysicsFramesAsync(2);
        controller.RefreshOcclusion();
        Node3D level = main.GetNode<Node3D>("%TestLevel3D");
        CameraOccluder3D occluder =
            level.GetNode<CameraOccluder3D>(expectedOccluderPath);
        TestAssert.True(
            (occluder.CollisionLayer & CollisionLayers3D.CameraOccluder) != 0,
            $"'{expectedOccluderPath}' is not configured for occlusion near {position}.");

        HashSet<CameraOccluder3D> silhouetteHits = new();
        Godot.Collections.Array<Rid> exclusions = new()
        {
            main.Player.GetRid()
        };
        Vector3 cameraRight = camera.GlobalTransform.Basis.X;
        cameraRight.Y = 0.0f;
        cameraRight = cameraRight.Normalized();
        Vector3[] silhouettePoints =
        {
            position + (Vector3.Up * controller.CentreRayHeight),
            position + (Vector3.Up * controller.UpperRayHeight),
            position + (Vector3.Up * controller.CentreRayHeight) +
                (cameraRight * controller.SilhouetteHalfWidth),
            position + (Vector3.Up * controller.CentreRayHeight) -
                (cameraRight * controller.SilhouetteHalfWidth),
            position + (Vector3.Up * controller.LowerRayHeight)
        };
        for (int pointIndex = 0;
             pointIndex < silhouettePoints.Length;
             pointIndex++)
        {
            PhysicsRayQueryParameters3D query =
                PhysicsRayQueryParameters3D.Create(
                    camera.GlobalPosition,
                    silhouettePoints[pointIndex],
                    CollisionLayers3D.CameraOccluder,
                    exclusions);
            query.CollideWithAreas = false;
            query.CollideWithBodies = true;
            for (int hitIndex = 0; hitIndex < 8; hitIndex++)
            {
                query.Exclude = exclusions;
                Godot.Collections.Dictionary result =
                    main.Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
                if (result.Count == 0 ||
                    !result.TryGetValue("collider", out Variant colliderVariant) ||
                    colliderVariant.AsGodotObject() is not CameraOccluder3D hit)
                {
                    break;
                }

                silhouetteHits.Add(hit);
                exclusions.Add(hit.GetRid());
            }
        }

        foreach (CameraOccluder3D hit in silhouetteHits)
        {
            TestAssert.True(hit.IsOccluded,
                $"Occluder '{hit.Name}' remained opaque over the player at {position}.");
        }
        TestAssert.True(
            controller.FadedOccluderCount >= silhouetteHits.Count,
            $"Faded HUD count omitted an obstruction at {position}.");

        main.Player.GlobalPosition = new Vector3(0.0f, 0.05f, 0.0f);
        camera._Process(1.0 / 60.0);
        await context.WaitPhysicsFramesAsync(2);
        controller.RefreshOcclusion();
        controller.RefreshOcclusion();
    }

    private static CharacterBody3D CreateTargetBody()
    {
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
        return target;
    }

    private static CameraOccluder3D CreateOccluder(
        string name,
        Vector3 position,
        StandardMaterial3D material,
        out MeshInstance3D firstMesh,
        out CollisionShape3D collision,
        bool includeSecondVisual,
        out MeshInstance3D? secondMesh,
        Vector3? size = null)
    {
        Vector3 resolvedSize = size ?? new Vector3(2.0f, 5.0f, 0.3f);
        CameraOccluder3D occluder = new()
        {
            Name = name,
            Position = position,
            CollisionLayer = CollisionLayers3D.World |
                             CollisionLayers3D.CameraOccluder,
            CollisionMask = 0
        };
        firstMesh = new MeshInstance3D
        {
            Name = "OccluderMesh3D",
            Mesh = new BoxMesh
            {
                Material = material,
                Size = resolvedSize
            }
        };
        collision = new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = new BoxShape3D { Size = resolvedSize }
        };
        occluder.AddChild(firstMesh);
        if (includeSecondVisual)
        {
            secondMesh = new MeshInstance3D
            {
                Name = "AdditionalOccluderMesh3D",
                Position = new Vector3(0.0f, 0.0f, 0.2f),
                Mesh = new BoxMesh
                {
                    Material = material,
                    Size = resolvedSize * 0.5f
                }
            };
            occluder.AddChild(secondMesh);
        }
        else
        {
            secondMesh = null;
        }

        occluder.AddChild(collision);
        return occluder;
    }

    private static System.Collections.Generic.IEnumerable<MeshInstance3D>
        EnumerateMeshDescendants(Node root)
    {
        for (int index = 0; index < root.GetChildCount(); index++)
        {
            Node child = root.GetChild(index);
            if (child is MeshInstance3D mesh)
            {
                yield return mesh;
            }

            foreach (MeshInstance3D nested in EnumerateMeshDescendants(child))
            {
                yield return nested;
            }
        }
    }

    private static MeshInstance3D GetShadowProxy(MeshInstance3D source)
    {
        for (int index = 0; index < source.GetChildCount(); index++)
        {
            if (source.GetChild(index) is MeshInstance3D candidate &&
                candidate.CastShadow ==
                GeometryInstance3D.ShadowCastingSetting.ShadowsOnly)
            {
                return candidate;
            }
        }

        throw new TestAssertionException(
            $"Occluder visual '{source.Name}' has no shadow-only proxy.");
    }

    private static int CountShadowProxyChildren(MeshInstance3D source)
    {
        int count = 0;
        for (int index = 0; index < source.GetChildCount(); index++)
        {
            if (source.GetChild(index) is MeshInstance3D candidate &&
                candidate.CastShadow ==
                GeometryInstance3D.ShadowCastingSetting.ShadowsOnly)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertActiveShadowProxy(
        MeshInstance3D source,
        MeshInstance3D proxy,
        Material expectedMaterial,
        string context)
    {
        TestAssert.True(proxy.Visible,
            $"{context} shadow proxy is not active.");
        TestAssert.True(ReferenceEquals(source, proxy.GetParent()),
            $"{context} shadow proxy is not transform-locked to its source visual.");
        TestAssert.True(ReferenceEquals(source.Mesh, proxy.Mesh),
            $"{context} shadow proxy does not use the exact source mesh.");
        TestAssert.Equal(source.Layers, proxy.Layers,
            $"{context} shadow proxy changed the source render layers.");
        TestAssert.Equal(Vector3.Zero, proxy.Position,
            $"{context} shadow proxy has a local position offset.");
        TestAssert.Equal(Vector3.Zero, proxy.Rotation,
            $"{context} shadow proxy has a local rotation offset.");
        TestAssert.Equal(Vector3.One, proxy.Scale,
            $"{context} shadow proxy has a local scale offset.");
        TestAssert.Equal(
            GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
            proxy.CastShadow,
            $"{context} proxy is not shadow-only geometry.");
        TestAssert.True(
            ReferenceEquals(expectedMaterial, proxy.GetActiveMaterial(0)),
            $"{context} proxy does not use the exact original opaque material.");
        TestAssert.NearlyEqual(0.0, proxy.Transparency, Tolerance,
            $"{context} proxy incorrectly uses geometry transparency.");
    }

    private static StandardMaterial3D CreateMaterial()
    {
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.3f, 0.35f, 1.0f),
            Roughness = 0.8f
        };
    }

    private static MeshInstance3D GetFirstMesh(Node root)
    {
        for (int index = 0; index < root.GetChildCount(); index++)
        {
            if (root.GetChild(index) is MeshInstance3D mesh)
            {
                return mesh;
            }
        }

        throw new TestAssertionException(
            $"Occluder '{root.Name}' has no configured mesh visual.");
    }

    private static void AssertPositionalShadowLight(
        Light3D light,
        float actualRange,
        float expectedRange,
        string name)
    {
        TestAssert.True(light.ShadowEnabled,
            $"{name} no longer casts required world shadows.");
        TestAssert.False(light.DistanceFadeEnabled,
            $"{name} shadows depend on camera distance fade.");
        TestAssert.NearlyEqual(expectedRange,
            actualRange,
            Tolerance,
            $"{name} range changed without level-layout justification.");
        TestAssert.True(
            light.ShadowOpacity > 0.0f &&
            light.ShadowOpacity < 0.8f,
            $"{name} shadow opacity is outside the controlled range.");
    }

    private static void AssertReducedShadow(Light3D light, string name)
    {
        TestAssert.True(light.ShadowEnabled,
            $"{name} no longer casts useful world shadows.");
        TestAssert.True(light.ShadowOpacity > 0.0f && light.ShadowOpacity < 0.8f,
            $"{name} shadow opacity was not reduced to a controlled range.");
    }
}
