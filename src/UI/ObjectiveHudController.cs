using System;
using Godot;
using LineZero.Gameplay.Objectives;

namespace LineZero.UI;

public sealed partial class ObjectiveHudController : MarginContainer
{
    private Label _objectiveLabel = null!;
    private Label _statusLabel = null!;
    private ObjectiveProgressModel? _objectives;

    public override void _Ready()
    {
        _objectiveLabel = RequireNode<Label>("%ObjectiveLabel");
        _statusLabel = RequireNode<Label>("%ObjectiveStatusLabel");
        SetUnboundDisplay();
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    public void Bind(ObjectiveProgressModel objectives)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        if (ReferenceEquals(_objectives, objectives))
        {
            Refresh(objectives.CurrentStage);
            return;
        }

        Unbind();
        _objectives = objectives;
        _objectives.Changed += OnObjectiveChanged;
        Refresh(_objectives.CurrentStage);
    }

    private void OnObjectiveChanged(ObjectiveStage previous, ObjectiveStage current)
    {
        Refresh(current);
    }

    private void Refresh(ObjectiveStage stage)
    {
        _objectiveLabel.Text = stage switch
        {
            ObjectiveStage.FindFuse => "FIND A REPLACEMENT FUSE",
            ObjectiveStage.RestorePower => "RESTORE POWER AT THE MAINTENANCE PANEL",
            ObjectiveStage.OpenExit => "OPEN THE EMERGENCY EXIT",
            ObjectiveStage.ReachExit => "REACH THE EXIT",
            ObjectiveStage.Completed => "ESCAPE COMPLETE",
            _ => throw new InvalidOperationException("Unknown objective stage.")
        };
        _statusLabel.Text = stage == ObjectiveStage.Completed
            ? "COMPLETED"
            : "CURRENT OBJECTIVE";
    }

    private void Unbind()
    {
        if (_objectives is not null)
        {
            _objectives.Changed -= OnObjectiveChanged;
        }

        _objectives = null;
        if (GodotObject.IsInstanceValid(_objectiveLabel))
        {
            SetUnboundDisplay();
        }
    }

    private void SetUnboundDisplay()
    {
        _objectiveLabel.Text = "OBJECTIVE UNAVAILABLE";
        _statusLabel.Text = "OFFLINE";
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(ObjectiveHudController)} on '{Name}' requires '{path}'.");
    }
}
