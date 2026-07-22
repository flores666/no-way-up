using System;
using Godot;
using LineZero.Gameplay.Perception;

namespace LineZero.UI;

public sealed partial class VisibilityHudController : MarginContainer
{
    private static readonly Color HiddenColor = new(0.50f, 0.72f, 0.75f, 1.0f);
    private static readonly Color DimColor = new(0.64f, 0.82f, 0.77f, 1.0f);
    private static readonly Color VisibleColor = new(0.92f, 0.82f, 0.45f, 1.0f);
    private static readonly Color ExposedColor = new(0.95f, 0.46f, 0.30f, 1.0f);
    private static readonly Color DeadColor = new(0.55f, 0.57f, 0.56f, 1.0f);

    private Label _valueLabel = null!;
    private Label _zoneLabel = null!;
    private IVisibilityStateSource? _visibility;

    public override void _Ready()
    {
        _valueLabel = RequireNode<Label>("%VisibilityValueLabel");
        _zoneLabel = RequireNode<Label>("%VisibilityZoneLabel");
        SetUnboundDisplay();
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    public void Bind(IVisibilityStateSource visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        if (ReferenceEquals(_visibility, visibility))
        {
            Refresh(visibility.State);
            return;
        }

        Unbind();
        _visibility = visibility;
        _visibility.VisibilityChanged += OnVisibilityChanged;
        Refresh(_visibility.State);
    }

    private void Unbind()
    {
        if (_visibility is not null)
        {
            _visibility.VisibilityChanged -= OnVisibilityChanged;
        }

        _visibility = null;
        if (GodotObject.IsInstanceValid(_valueLabel))
        {
            SetUnboundDisplay();
        }
    }

    private void OnVisibilityChanged(VisibilityState state)
    {
        Refresh(state);
    }

    private void Refresh(VisibilityState state)
    {
        if (!state.IsActorAlive)
        {
            _valueLabel.Text = "VISIBILITY: DEAD";
            _valueLabel.Modulate = DeadColor;
            _zoneLabel.Text = state.AmbientZoneName.ToUpperInvariant();
            return;
        }

        string category = state.Category.ToString().ToUpperInvariant();
        _valueLabel.Text = $"VISIBILITY: {category} {state.FinalMultiplier:0.00}x";
        _zoneLabel.Text = state.AmbientZoneName.ToUpperInvariant();
        _valueLabel.Modulate = state.Category switch
        {
            VisibilityCategory.Hidden => HiddenColor,
            VisibilityCategory.Dim => DimColor,
            VisibilityCategory.Visible => VisibleColor,
            VisibilityCategory.Exposed => ExposedColor,
            _ => throw new InvalidOperationException("Unknown visibility category.")
        };
    }

    private void SetUnboundDisplay()
    {
        _valueLabel.Text = "VISIBILITY: --";
        _valueLabel.Modulate = DeadColor;
        _zoneLabel.Text = "UNAVAILABLE";
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(VisibilityHudController)} on '{Name}' requires '{path}'.");
    }
}
