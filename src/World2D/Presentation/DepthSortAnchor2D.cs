using System;
using Godot;

namespace LineZero.World2D.Presentation;

public sealed partial class DepthSortAnchor2D : Node
{
    private const int CanvasZMin = -4096;
    private const int CanvasZMax = 4096;

    public const int DefaultBaseZIndex = 96;
    public const float DefaultWorldPixelsPerZLayer = 8.0f;
    public const int DefaultMinimumZIndex = 16;
    public const int DefaultMaximumZIndex = 240;

    [Export]
    public int BaseZIndex { get; set; } = DefaultBaseZIndex;

    [Export(PropertyHint.Range, "1,32,0.5,or_greater")]
    public float WorldPixelsPerZLayer { get; set; } = DefaultWorldPixelsPerZLayer;

    [Export]
    public int MinimumZIndex { get; set; } = DefaultMinimumZIndex;

    [Export]
    public int MaximumZIndex { get; set; } = DefaultMaximumZIndex;

    [Export]
    public float SortOffsetY { get; set; } = 12.0f;

    [Export]
    public bool UpdateContinuously { get; set; } = true;

    private CanvasItem _target = null!;
    private Node2D _targetNode = null!;

    public override void _Ready()
    {
        _target = GetParent() as CanvasItem
            ?? throw new InvalidOperationException(
                $"{nameof(DepthSortAnchor2D)} on '{Name}' requires a CanvasItem parent.");
        _targetNode = GetParent() as Node2D
            ?? throw new InvalidOperationException(
                $"{nameof(DepthSortAnchor2D)} on '{Name}' requires a Node2D parent.");

        ValidateConfiguration();

        _target.ZAsRelative = false;
        UpdateDepth();
        SetProcess(UpdateContinuously);
    }

    public override void _Process(double delta)
    {
        _ = delta;
        UpdateDepth();
    }

    public static int CalculateDepth(
        float worldY,
        int baseZIndex = DefaultBaseZIndex,
        float worldPixelsPerZLayer = DefaultWorldPixelsPerZLayer,
        int minimumZIndex = DefaultMinimumZIndex,
        int maximumZIndex = DefaultMaximumZIndex)
    {
        if (!float.IsFinite(worldY))
        {
            throw new ArgumentOutOfRangeException(nameof(worldY));
        }

        if (!float.IsFinite(worldPixelsPerZLayer) || worldPixelsPerZLayer <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(worldPixelsPerZLayer));
        }

        if (minimumZIndex < CanvasZMin || maximumZIndex > CanvasZMax ||
            minimumZIndex >= maximumZIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumZIndex));
        }

        int depthOffset = Mathf.RoundToInt(worldY / worldPixelsPerZLayer);
        return Math.Clamp(baseZIndex + depthOffset, minimumZIndex, maximumZIndex);
    }

    private void ValidateConfiguration()
    {
        _ = CalculateDepth(
            0.0f,
            BaseZIndex,
            WorldPixelsPerZLayer,
            MinimumZIndex,
            MaximumZIndex);
    }

    private void UpdateDepth()
    {
        float worldY = _targetNode.GlobalPosition.Y + SortOffsetY;
        _target.ZIndex = CalculateDepth(
            worldY,
            BaseZIndex,
            WorldPixelsPerZLayer,
            MinimumZIndex,
            MaximumZIndex);
    }
}
