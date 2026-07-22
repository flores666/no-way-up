using System;
using Godot;

namespace LineZero.World3D;

public sealed partial class CameraOccluder3D : StaticBody3D
{
    private MeshInstance3D _occluderMesh = null!;

    [Export(PropertyHint.Range, "0.0,1.0,0.05")]
    public float OccludedTransparency { get; set; } = 0.72f;

    public bool IsOccluded { get; private set; }

    public override void _Ready()
    {
        if (!float.IsFinite(OccludedTransparency) ||
            OccludedTransparency < 0.0f ||
            OccludedTransparency > 1.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(OccludedTransparency)} must be within 0..1.");
        }

        if ((CollisionLayer & CollisionLayers3D.CameraOccluder) == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(CameraOccluder3D)} on '{Name}' requires the camera-occluder layer.");
        }

        _occluderMesh = GetNodeOrNull<MeshInstance3D>("OccluderMesh3D")
            ?? throw new InvalidOperationException(
                $"{nameof(CameraOccluder3D)} on '{Name}' requires OccluderMesh3D.");
        ApplyState();
    }

    public void SetOccluded(bool occluded)
    {
        if (IsOccluded == occluded)
        {
            return;
        }

        IsOccluded = occluded;
        ApplyState();
    }

    private void ApplyState()
    {
        if (_occluderMesh is null)
        {
            return;
        }

        _occluderMesh.Transparency = IsOccluded
            ? OccludedTransparency
            : 0.0f;
    }
}
