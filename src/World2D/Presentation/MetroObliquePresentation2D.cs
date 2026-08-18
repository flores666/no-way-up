using System;
using System.Collections.Generic;
using Godot;

namespace LineZero.World2D.Presentation;

public sealed partial class MetroObliquePresentation2D : Node2D
{
    [Export]
    public NodePath WallsPath { get; set; } = new("../WallsVisual");

    [Export]
    public NodePath FloorPath { get; set; } = new("../Floor");

    [Export]
    public float DefaultWallHeight { get; set; } = 34.0f;

    [Export]
    public float FloorDetailSpacing { get; set; } = 36.0f;

    [Export]
    public int DepthBaseZIndex { get; set; } = 1000;

    [Export]
    public Vector2 ShadowOffset { get; set; } = new(10.0f, 12.0f);

    public override void _Ready()
    {
        if (!float.IsFinite(DefaultWallHeight) || DefaultWallHeight <= 0.0f ||
            !float.IsFinite(FloorDetailSpacing) || FloorDetailSpacing < 16.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(MetroObliquePresentation2D)} on '{Name}' requires valid presentation dimensions.");
        }

        Node2D floor = GetNodeOrNull<Node2D>(FloorPath)
            ?? throw new InvalidOperationException(
                $"{nameof(MetroObliquePresentation2D)} on '{Name}' cannot resolve '{FloorPath}'.");
        Node2D walls = GetNodeOrNull<Node2D>(WallsPath)
            ?? throw new InvalidOperationException(
                $"{nameof(MetroObliquePresentation2D)} on '{Name}' cannot resolve '{WallsPath}'.");

        BuildFloorDetails(floor);

        List<Polygon2D> authoredWalls = new();
        for (int index = 0; index < walls.GetChildCount(); index++)
        {
            if (walls.GetChild(index) is Polygon2D wall)
            {
                authoredWalls.Add(wall);
            }
        }

        for (int index = 0; index < authoredWalls.Count; index++)
        {
            BuildExtrudedWall(walls, authoredWalls[index]);
        }
    }

    private void BuildFloorDetails(Node2D floor)
    {
        Node2D details = new()
        {
            Name = "GeneratedGroundDetails",
            ZAsRelative = false,
            ZIndex = -10,
        };
        AddChild(details);

        for (int index = 0; index < floor.GetChildCount(); index++)
        {
            if (floor.GetChild(index) is not Polygon2D surface ||
                surface.Name.ToString().Contains("Overlay", StringComparison.Ordinal))
            {
                continue;
            }

            Rect2 bounds = CalculateBounds(surface.Polygon);
            if (surface.Name.ToString().Contains("TrackTie", StringComparison.Ordinal) ||
                bounds.Size.X < 80.0f ||
                bounds.Size.Y < 60.0f)
            {
                continue;
            }

            Vector2 origin = surface.Position;
            Color seamColor = new(
                Mathf.Max(0.0f, surface.Color.R - 0.07f),
                Mathf.Max(0.0f, surface.Color.G - 0.07f),
                Mathf.Max(0.0f, surface.Color.B - 0.07f),
                0.36f);

            int row = 0;
            for (float y = bounds.Position.Y + FloorDetailSpacing;
                 y < bounds.End.Y;
                 y += FloorDetailSpacing, row++)
            {
                AddLine(
                    details,
                    $"{surface.Name}Row{row}",
                    origin + new Vector2(bounds.Position.X, y),
                    origin + new Vector2(bounds.End.X, y),
                    seamColor,
                    1.0f);
            }

            int column = 0;
            float halfSpacing = FloorDetailSpacing * 0.5f;
            for (float x = bounds.Position.X + halfSpacing;
                 x < bounds.End.X;
                 x += FloorDetailSpacing, column++)
            {
                float offset = column % 2 == 0 ? 0.0f : halfSpacing;
                for (float y = bounds.Position.Y + offset;
                     y < bounds.End.Y;
                     y += FloorDetailSpacing)
                {
                    float segmentEnd = Mathf.Min(y + FloorDetailSpacing, bounds.End.Y);
                    AddLine(
                        details,
                        $"{surface.Name}Joint{column}_{Mathf.RoundToInt(y)}",
                        origin + new Vector2(x, y),
                        origin + new Vector2(x, segmentEnd),
                        seamColor,
                        1.0f);
                }
            }

            AddSurfaceStains(details, surface, bounds, origin);
        }
    }

    private static void AddSurfaceStains(
        Node2D parent,
        Polygon2D surface,
        Rect2 bounds,
        Vector2 origin)
    {
        int stainCount = Math.Clamp(Mathf.RoundToInt((bounds.Size.X * bounds.Size.Y) / 180000.0f), 1, 5);
        int seed = StableHash(surface.Name.ToString());
        for (int index = 0; index < stainCount; index++)
        {
            float xRatio = PositiveFraction(seed * (index + 3) * 0.000173f);
            float yRatio = PositiveFraction(seed * (index + 7) * 0.000097f);
            Vector2 center = origin + new Vector2(
                Mathf.Lerp(bounds.Position.X + 18.0f, bounds.End.X - 18.0f, xRatio),
                Mathf.Lerp(bounds.Position.Y + 18.0f, bounds.End.Y - 18.0f, yRatio));
            float radiusX = 10.0f + 9.0f * PositiveFraction(seed * (index + 11) * 0.000131f);
            float radiusY = 5.0f + 6.0f * PositiveFraction(seed * (index + 17) * 0.000211f);
            Polygon2D stain = new()
            {
                Name = $"{surface.Name}Stain{index}",
                Position = center,
                Polygon = CreateEllipse(radiusX, radiusY, 10),
                Color = new Color(0.04f, 0.05f, 0.048f, 0.2f),
            };
            parent.AddChild(stain);
        }
    }

    private static void AddLine(
        Node2D parent,
        string name,
        Vector2 from,
        Vector2 to,
        Color color,
        float width)
    {
        Line2D line = new()
        {
            Name = name,
            Points = [from, to],
            Width = width,
            DefaultColor = color,
        };
        parent.AddChild(line);
    }

    private static Vector2[] CreateEllipse(float radiusX, float radiusY, int segments)
    {
        Vector2[] points = new Vector2[segments];
        for (int index = 0; index < segments; index++)
        {
            float angle = Mathf.Tau * index / segments;
            points[index] = new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
        }

        return points;
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            int hash = 17;
            for (int index = 0; index < value.Length; index++)
            {
                hash = hash * 31 + value[index];
            }

            return hash;
        }
    }

    private static float PositiveFraction(float value)
    {
        return value - Mathf.Floor(value);
    }

    private void BuildExtrudedWall(Node2D parent, Polygon2D source)
    {
        Vector2[] footprint = source.Polygon;
        if (footprint.Length < 3)
        {
            throw new InvalidOperationException(
                $"Wall '{source.GetPath()}' requires at least three polygon points.");
        }

        Rect2 bounds = CalculateBounds(footprint);
        float height = ResolveHeight(source.Name.ToString());
        int depth = Math.Clamp(
            DepthBaseZIndex + Mathf.RoundToInt(source.Position.Y + bounds.End.Y),
            -4096,
            4096 - 4);

        Node2D generated = new()
        {
            Name = $"{source.Name}Oblique",
            Position = source.Position,
            ZAsRelative = false,
            ZIndex = depth,
        };
        parent.AddChild(generated);

        Color baseColor = source.Color;
        AddPolygon(
            generated,
            "Shadow",
            OffsetPolygon(footprint, ShadowOffset),
            new Color(0.008f, 0.012f, 0.016f, 0.42f),
            -3);
        AddPolygon(
            generated,
            "FrontFace",
            CreateFrontFace(bounds, height),
            baseColor.Lightened(0.02f),
            0);
        AddPolygon(
            generated,
            "RightFace",
            CreateRightFace(bounds, height),
            baseColor.Darkened(0.19f),
            1);
        AddPolygon(
            generated,
            "TopFace",
            OffsetPolygon(footprint, new Vector2(0.0f, -height)),
            baseColor.Lightened(0.24f),
            2);

        Line2D outline = new()
        {
            Name = "TopOutline",
            Points = ClosePolygon(OffsetPolygon(footprint, new Vector2(0.0f, -height))),
            Width = 1.5f,
            DefaultColor = new Color(0.02f, 0.027f, 0.032f, 0.78f),
            ZIndex = 3,
        };
        generated.AddChild(outline);
        source.Visible = false;
    }

    private float ResolveHeight(string wallName)
    {
        if (wallName.Contains("Column", StringComparison.Ordinal))
        {
            return DefaultWallHeight * 1.45f;
        }

        if (wallName.Contains("Booth", StringComparison.Ordinal))
        {
            return DefaultWallHeight * 1.2f;
        }

        if (wallName.Contains("MaintenancePassage", StringComparison.Ordinal))
        {
            return DefaultWallHeight * 0.48f;
        }

        if (wallName.Contains("Barricade", StringComparison.Ordinal))
        {
            return DefaultWallHeight * 0.35f;
        }

        return DefaultWallHeight;
    }

    private static Rect2 CalculateBounds(Vector2[] points)
    {
        Vector2 minimum = points[0];
        Vector2 maximum = points[0];
        for (int index = 1; index < points.Length; index++)
        {
            minimum = new Vector2(
                Mathf.Min(minimum.X, points[index].X),
                Mathf.Min(minimum.Y, points[index].Y));
            maximum = new Vector2(
                Mathf.Max(maximum.X, points[index].X),
                Mathf.Max(maximum.Y, points[index].Y));
        }

        return new Rect2(minimum, maximum - minimum);
    }

    private static Vector2[] CreateFrontFace(Rect2 bounds, float height)
    {
        return
        [
            new Vector2(bounds.Position.X, bounds.End.Y),
            new Vector2(bounds.End.X, bounds.End.Y),
            new Vector2(bounds.End.X, bounds.End.Y - height),
            new Vector2(bounds.Position.X, bounds.End.Y - height),
        ];
    }

    private static Vector2[] CreateRightFace(Rect2 bounds, float height)
    {
        return
        [
            new Vector2(bounds.End.X, bounds.Position.Y),
            new Vector2(bounds.End.X, bounds.End.Y),
            new Vector2(bounds.End.X, bounds.End.Y - height),
            new Vector2(bounds.End.X, bounds.Position.Y - height),
        ];
    }

    private static Vector2[] OffsetPolygon(Vector2[] source, Vector2 offset)
    {
        Vector2[] result = new Vector2[source.Length];
        for (int index = 0; index < source.Length; index++)
        {
            result[index] = source[index] + offset;
        }

        return result;
    }

    private static Vector2[] ClosePolygon(Vector2[] source)
    {
        Vector2[] closed = new Vector2[source.Length + 1];
        Array.Copy(source, closed, source.Length);
        closed[^1] = source[0];
        return closed;
    }

    private static void AddPolygon(
        Node2D parent,
        string name,
        Vector2[] points,
        Color color,
        int zIndex)
    {
        Polygon2D polygon = new()
        {
            Name = name,
            Polygon = points,
            Color = color,
            ZIndex = zIndex,
        };
        parent.AddChild(polygon);
    }
}
