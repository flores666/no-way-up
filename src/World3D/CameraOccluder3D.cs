using System;
using System.Collections.Generic;
using Godot;

namespace LineZero.World3D;

public sealed partial class CameraOccluder3D : StaticBody3D
{
    private const float FadeCompletionEpsilon = 0.001f;
    private const string GeneratedShadowProxyMeta =
        "line_zero_camera_occluder_shadow_proxy";

    private readonly List<MeshInstance3D> _sourceVisualBuffer = new(4);
    private readonly List<OccluderVisualState> _visualStates = new(4);

    private float _currentFadeAmount;
    private bool _fadeOverridesApplied;

    [Export(PropertyHint.Range, "0.0,0.95,0.01")]
    public float OccludedTransparency { get; set; } = 0.72f;

    [Export(PropertyHint.Range, "1.0,30.0,0.5")]
    public float FadeSpeed { get; set; } = 12.0f;

    [Export]
    public NodePath VisualRootPath { get; set; } = new(".");

    public bool IsOccluded { get; private set; }

    public float CurrentFadeAmount => _currentFadeAmount;

    public int ConfiguredVisualCount => _visualStates.Count;

    public int ConfiguredShadowProxyCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < _visualStates.Count; index++)
            {
                count += _visualStates[index].ShadowProxy is null ? 0 : 1;
            }

            return count;
        }
    }

    public int ActiveShadowProxyCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < _visualStates.Count; index++)
            {
                MeshInstance3D? proxy = _visualStates[index].ShadowProxy;
                count += proxy is not null && proxy.Visible ? 1 : 0;
            }

            return count;
        }
    }

    public override void _Ready()
    {
        ValidateConfiguration();
        Node visualRoot = GetNodeOrNull<Node>(VisualRootPath)
            ?? throw new InvalidOperationException(
                $"{nameof(CameraOccluder3D)} on '{Name}' cannot resolve " +
                $"{nameof(VisualRootPath)} '{VisualRootPath}'.");

        _sourceVisualBuffer.Clear();
        _visualStates.Clear();
        CollectSourceVisuals(visualRoot);
        for (int index = 0; index < _sourceVisualBuffer.Count; index++)
        {
            _visualStates.Add(CreateVisualState(_sourceVisualBuffer[index]));
        }

        _sourceVisualBuffer.Clear();
        if (_visualStates.Count == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(CameraOccluder3D)} on '{Name}' requires at least " +
                "one MeshInstance3D below its configured visual root.");
        }

        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        float targetFade = IsOccluded ? 1.0f : 0.0f;
        float blend = 1.0f - MathF.Exp(-FadeSpeed * (float)delta);
        float nextFade = Mathf.Lerp(_currentFadeAmount, targetFade, blend);
        if (Mathf.Abs(nextFade - targetFade) <= FadeCompletionEpsilon)
        {
            nextFade = targetFade;
        }

        ApplyFadeAmount(nextFade);
        if (Mathf.IsEqualApprox(nextFade, targetFade))
        {
            SetProcess(false);
        }
    }

    public override void _ExitTree()
    {
        SetProcess(false);
        RestoreExactOriginalState();
        IsOccluded = false;
        _sourceVisualBuffer.Clear();
        _visualStates.Clear();
    }

    public void SetOccluded(bool occluded)
    {
        if (IsOccluded == occluded)
        {
            return;
        }

        IsOccluded = occluded;
        if (occluded)
        {
            // Install the transparent visual material and switch shadow ownership
            // in one completed presentation step. This prevents a one-frame shadow
            // gap before the first fade process tick.
            EnsureFadeOverridesApplied();
        }
        else if (_currentFadeAmount <= FadeCompletionEpsilon)
        {
            ApplyFadeAmount(0.0f);
            return;
        }

        SetProcess(true);
    }

    private void ValidateConfiguration()
    {
        if (!float.IsFinite(OccludedTransparency) ||
            OccludedTransparency < 0.0f ||
            OccludedTransparency > 0.95f)
        {
            throw new InvalidOperationException(
                $"{nameof(OccludedTransparency)} must be within 0..0.95.");
        }

        if (!float.IsFinite(FadeSpeed) || FadeSpeed < 1.0f || FadeSpeed > 30.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(FadeSpeed)} must be within 1..30.");
        }

        if (VisualRootPath.IsEmpty)
        {
            throw new InvalidOperationException(
                $"{nameof(VisualRootPath)} must be configured explicitly.");
        }

        uint requiredLayers = CollisionLayers3D.World |
                              CollisionLayers3D.CameraOccluder;
        if ((CollisionLayer & requiredLayers) != requiredLayers)
        {
            throw new InvalidOperationException(
                $"{nameof(CameraOccluder3D)} on '{Name}' requires both " +
                "World and CameraOccluder collision layers.");
        }
    }

    private void CollectSourceVisuals(Node node)
    {
        if (node is MeshInstance3D mesh && !IsGeneratedShadowProxy(mesh))
        {
            _sourceVisualBuffer.Add(mesh);
        }

        for (int index = 0; index < node.GetChildCount(); index++)
        {
            CollectSourceVisuals(node.GetChild(index));
        }
    }

    private OccluderVisualState CreateVisualState(MeshInstance3D mesh)
    {
        Mesh meshResource = mesh.Mesh
            ?? throw new InvalidOperationException(
                $"Occluder visual '{mesh.GetPath()}' requires a Mesh resource.");
        GeometryInstance3D.ShadowCastingSetting originalShadowMode =
            mesh.CastShadow;
        OccluderVisualState state = new(
            mesh,
            originalShadowMode,
            mesh.MaterialOverride,
            mesh.MaterialOverlay,
            CreateOrConfigureShadowProxy(
                mesh,
                meshResource,
                originalShadowMode));
        if (mesh.MaterialOverlay is not null)
        {
            state.FadeMaterialOverlay = CreateFadeMaterial(
                mesh.MaterialOverlay,
                $"material overlay on '{mesh.GetPath()}'");
            state.FadeMaterialOverlayOriginalAlbedo =
                state.FadeMaterialOverlay.AlbedoColor;
        }

        if (mesh.MaterialOverride is not null)
        {
            state.FadeMaterialOverride = CreateFadeMaterial(
                mesh.MaterialOverride,
                $"material override on '{mesh.GetPath()}'");
            state.FadeMaterialOverrideOriginalAlbedo =
                state.FadeMaterialOverride.AlbedoColor;
            return state;
        }

        int surfaceCount = meshResource.GetSurfaceCount();
        if (surfaceCount == 0)
        {
            throw new InvalidOperationException(
                $"Occluder visual '{mesh.GetPath()}' has no render surfaces.");
        }

        for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
        {
            Material? originalOverride =
                mesh.GetSurfaceOverrideMaterial(surfaceIndex);
            Material? source = originalOverride ??
                               meshResource.SurfaceGetMaterial(surfaceIndex);
            StandardMaterial3D fadeMaterial = CreateFadeMaterial(
                source,
                $"surface {surfaceIndex} on '{mesh.GetPath()}'");
            state.SurfaceMaterials.Add(new SurfaceMaterialState(
                surfaceIndex,
                originalOverride,
                fadeMaterial,
                fadeMaterial.AlbedoColor));
        }

        return state;
    }

    private static MeshInstance3D? CreateOrConfigureShadowProxy(
        MeshInstance3D source,
        Mesh meshResource,
        GeometryInstance3D.ShadowCastingSetting originalShadowMode)
    {
        if (originalShadowMode == GeometryInstance3D.ShadowCastingSetting.Off ||
            originalShadowMode ==
            GeometryInstance3D.ShadowCastingSetting.ShadowsOnly)
        {
            return null;
        }

        MeshInstance3D? proxy = null;
        for (int index = 0; index < source.GetChildCount(); index++)
        {
            if (source.GetChild(index) is MeshInstance3D child &&
                IsGeneratedShadowProxy(child))
            {
                proxy = child;
                break;
            }
        }

        if (proxy is null)
        {
            proxy = new MeshInstance3D
            {
                Name = $"{source.Name}CameraShadowProxy3D"
            };
            proxy.SetMeta(GeneratedShadowProxyMeta, true);
            source.AddChild(proxy);
        }

        proxy.Mesh = meshResource;
        proxy.Layers = source.Layers;
        proxy.MaterialOverride = source.MaterialOverride;
        proxy.MaterialOverlay = source.MaterialOverlay;
        proxy.CastShadow =
            GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
        proxy.Visible = false;
        proxy.Position = Vector3.Zero;
        proxy.Rotation = Vector3.Zero;
        proxy.Scale = Vector3.One;
        proxy.ExtraCullMargin = source.ExtraCullMargin;

        int surfaceCount = meshResource.GetSurfaceCount();
        for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
        {
            proxy.SetSurfaceOverrideMaterial(
                surfaceIndex,
                source.GetSurfaceOverrideMaterial(surfaceIndex));
        }

        return proxy;
    }

    private static bool IsGeneratedShadowProxy(MeshInstance3D mesh)
    {
        return mesh.HasMeta(GeneratedShadowProxyMeta);
    }

    private static StandardMaterial3D CreateFadeMaterial(
        Material? source,
        string context)
    {
        StandardMaterial3D material;
        if (source is null)
        {
            material = new StandardMaterial3D();
        }
        else if (source is StandardMaterial3D standardMaterial)
        {
            material = standardMaterial.Duplicate() as StandardMaterial3D
                ?? throw new InvalidOperationException(
                    $"Could not duplicate StandardMaterial3D for {context}.");
        }
        else
        {
            throw new InvalidOperationException(
                $"Camera occlusion requires StandardMaterial3D for {context}; " +
                $"received {source.GetType().Name}.");
        }

        material.ResourceLocalToScene = true;
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return material;
    }

    private void ApplyFadeAmount(float fadeAmount)
    {
        _currentFadeAmount = Mathf.Clamp(fadeAmount, 0.0f, 1.0f);
        if (_currentFadeAmount > 0.0f)
        {
            EnsureFadeOverridesApplied();
        }

        for (int visualIndex = 0;
             visualIndex < _visualStates.Count;
             visualIndex++)
        {
            OccluderVisualState visual = _visualStates[visualIndex];
            float alphaMultiplier =
                1.0f - (OccludedTransparency * _currentFadeAmount);
            if (visual.FadeMaterialOverride is not null)
            {
                Color original = visual.FadeMaterialOverrideOriginalAlbedo;
                original.A *= alphaMultiplier;
                visual.FadeMaterialOverride.AlbedoColor = original;
            }

            if (visual.FadeMaterialOverlay is not null)
            {
                Color original = visual.FadeMaterialOverlayOriginalAlbedo;
                original.A *= alphaMultiplier;
                visual.FadeMaterialOverlay.AlbedoColor = original;
            }

            for (int surfaceIndex = 0;
                 surfaceIndex < visual.SurfaceMaterials.Count;
                 surfaceIndex++)
            {
                SurfaceMaterialState surface =
                    visual.SurfaceMaterials[surfaceIndex];
                Color color = surface.OriginalAlbedo;
                color.A *= alphaMultiplier;
                surface.FadeMaterial.AlbedoColor = color;
            }
        }

        if (_currentFadeAmount <= FadeCompletionEpsilon && !IsOccluded)
        {
            RestoreExactOriginalState();
        }
    }

    private void EnsureFadeOverridesApplied()
    {
        if (_fadeOverridesApplied)
        {
            return;
        }

        for (int visualIndex = 0;
             visualIndex < _visualStates.Count;
             visualIndex++)
        {
            OccluderVisualState visual = _visualStates[visualIndex];
            if (visual.FadeMaterialOverlay is not null)
            {
                visual.Mesh.MaterialOverlay = visual.FadeMaterialOverlay;
            }

            if (visual.FadeMaterialOverride is not null)
            {
                visual.Mesh.MaterialOverride = visual.FadeMaterialOverride;
            }
            else
            {
                for (int surfaceIndex = 0;
                     surfaceIndex < visual.SurfaceMaterials.Count;
                     surfaceIndex++)
                {
                    SurfaceMaterialState surface =
                        visual.SurfaceMaterials[surfaceIndex];
                    visual.Mesh.SetSurfaceOverrideMaterial(
                        surface.SurfaceIndex,
                        surface.FadeMaterial);
                }
            }

            ActivateShadowProxy(visual);
        }

        _fadeOverridesApplied = true;
    }

    private static void ActivateShadowProxy(OccluderVisualState visual)
    {
        if (visual.ShadowProxy is null)
        {
            // Off remains off. ShadowsOnly already has no visible geometry and
            // therefore does not need a second proxy.
            return;
        }

        visual.Mesh.CastShadow =
            GeometryInstance3D.ShadowCastingSetting.Off;
        visual.ShadowProxy.Visible = true;
    }

    private static void RestoreOriginalShadowState(OccluderVisualState visual)
    {
        if (visual.ShadowProxy is not null)
        {
            visual.ShadowProxy.Visible = false;
        }

        visual.Mesh.CastShadow = visual.OriginalShadowMode;
    }

    private void RestoreExactOriginalState()
    {
        if (!_fadeOverridesApplied && _visualStates.Count == 0)
        {
            return;
        }

        for (int visualIndex = 0;
             visualIndex < _visualStates.Count;
             visualIndex++)
        {
            OccluderVisualState visual = _visualStates[visualIndex];
            RestoreOriginalShadowState(visual);
            visual.Mesh.MaterialOverlay = visual.OriginalMaterialOverlay;
            if (visual.FadeMaterialOverride is not null)
            {
                visual.Mesh.MaterialOverride = visual.OriginalMaterialOverride;
                continue;
            }

            for (int surfaceIndex = 0;
                 surfaceIndex < visual.SurfaceMaterials.Count;
                 surfaceIndex++)
            {
                SurfaceMaterialState surface =
                    visual.SurfaceMaterials[surfaceIndex];
                visual.Mesh.SetSurfaceOverrideMaterial(
                    surface.SurfaceIndex,
                    surface.OriginalOverride);
            }
        }

        _currentFadeAmount = 0.0f;
        _fadeOverridesApplied = false;
    }

    private sealed class OccluderVisualState
    {
        public OccluderVisualState(
            MeshInstance3D mesh,
            GeometryInstance3D.ShadowCastingSetting originalShadowMode,
            Material? originalMaterialOverride,
            Material? originalMaterialOverlay,
            MeshInstance3D? shadowProxy)
        {
            Mesh = mesh;
            OriginalShadowMode = originalShadowMode;
            OriginalMaterialOverride = originalMaterialOverride;
            OriginalMaterialOverlay = originalMaterialOverlay;
            ShadowProxy = shadowProxy;
        }

        public MeshInstance3D Mesh { get; }

        public GeometryInstance3D.ShadowCastingSetting OriginalShadowMode { get; }

        public Material? OriginalMaterialOverride { get; }

        public Material? OriginalMaterialOverlay { get; }

        public MeshInstance3D? ShadowProxy { get; }

        public StandardMaterial3D? FadeMaterialOverride { get; set; }

        public StandardMaterial3D? FadeMaterialOverlay { get; set; }

        public Color FadeMaterialOverrideOriginalAlbedo { get; set; } =
            Colors.White;

        public Color FadeMaterialOverlayOriginalAlbedo { get; set; } =
            Colors.White;

        public List<SurfaceMaterialState> SurfaceMaterials { get; } = new(2);
    }

    private sealed class SurfaceMaterialState
    {
        public SurfaceMaterialState(
            int surfaceIndex,
            Material? originalOverride,
            StandardMaterial3D fadeMaterial,
            Color originalAlbedo)
        {
            SurfaceIndex = surfaceIndex;
            OriginalOverride = originalOverride;
            FadeMaterial = fadeMaterial;
            OriginalAlbedo = originalAlbedo;
        }

        public int SurfaceIndex { get; }

        public Material? OriginalOverride { get; }

        public StandardMaterial3D FadeMaterial { get; }

        public Color OriginalAlbedo { get; }
    }
}
