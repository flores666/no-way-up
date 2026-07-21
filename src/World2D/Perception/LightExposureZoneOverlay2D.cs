using System;
using Godot;
using LineZero.Core.Scene;

namespace LineZero.World2D.Perception;

[GlobalClass]
public sealed partial class LightExposureZoneOverlay2D : Polygon2D
{
    [Export]
    public NodePath SourceShapePath { get; set; } = new();

    public override void _Ready()
    {
        CollisionShape2D sourceShape = RequiredNodePathResolver.Resolve<CollisionShape2D>(
            this,
            SourceShapePath,
            nameof(SourceShapePath));

        if (sourceShape.Disabled || sourceShape.Shape is not RectangleShape2D rectangle)
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureZoneOverlay2D)} on '{Name}' requires one enabled " +
                "RectangleShape2D source.");
        }

        Vector2 size = rectangle.Size;
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) ||
            size.X <= 0.0f || size.Y <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(LightExposureZoneOverlay2D)} on '{Name}' requires a positive " +
                "finite source rectangle size.");
        }

        Vector2 halfSize = size * 0.5f;
        Vector2[] sourceCorners =
        {
            new(-halfSize.X, -halfSize.Y),
            new(halfSize.X, -halfSize.Y),
            new(halfSize.X, halfSize.Y),
            new(-halfSize.X, halfSize.Y),
        };
        Vector2[] overlayPolygon = new Vector2[sourceCorners.Length];

        for (int index = 0; index < sourceCorners.Length; index++)
        {
            Vector2 worldCorner = sourceShape.ToGlobal(sourceCorners[index]);
            overlayPolygon[index] = ToLocal(worldCorner);
        }

        Polygon = overlayPolygon;
    }
}
