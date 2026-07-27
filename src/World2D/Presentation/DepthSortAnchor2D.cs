using System;
using Godot;

namespace LineZero.World2D.Presentation;

public sealed partial class DepthSortAnchor2D : Node
{
    [Export]
    public int BaseZIndex { get; set; } = 1000;

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

        _target.ZAsRelative = false;
        UpdateDepth();
        SetProcess(UpdateContinuously);
    }

    public override void _Process(double delta)
    {
        UpdateDepth();
    }

    private void UpdateDepth()
    {
        float worldY = _targetNode.GlobalPosition.Y + SortOffsetY;
        if (!float.IsFinite(worldY))
        {
            throw new InvalidOperationException(
                $"{nameof(DepthSortAnchor2D)} on '{Name}' received a non-finite world position.");
        }

        int depth = BaseZIndex + Mathf.RoundToInt(worldY);
        _target.ZIndex = Math.Clamp(depth, -4096, 4096);
    }
}
