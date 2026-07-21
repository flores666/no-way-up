using System;
using Godot;

namespace LineZero.UI;

public sealed partial class InteractionMessageController : MarginContainer
{
    private Label _messageLabel = null!;
    private Timer _hideTimer = null!;

    [Export(PropertyHint.Range, "0.25,15.0,0.25,or_greater")]
    public double DisplayDurationSeconds { get; set; } = 3.5;

    public override void _Ready()
    {
        if (DisplayDurationSeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(InteractionMessageController)} on '{Name}' requires a positive duration.");
        }

        _messageLabel = GetNodeOrNull<Label>("%MessageLabel")
            ?? throw new InvalidOperationException(
                $"{nameof(InteractionMessageController)} on '{Name}' requires a MessageLabel.");

        _hideTimer = GetNodeOrNull<Timer>("%HideTimer")
            ?? throw new InvalidOperationException(
                $"{nameof(InteractionMessageController)} on '{Name}' requires a HideTimer.");

        _hideTimer.Timeout += OnHideTimerTimeout;
        Visible = false;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_hideTimer))
        {
            _hideTimer.Timeout -= OnHideTimerTimeout;
        }
    }

    public void ShowMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("An interaction message cannot be empty.", nameof(message));
        }

        _hideTimer.Stop();
        _messageLabel.Text = message;
        Visible = true;
        _hideTimer.Start(DisplayDurationSeconds);
    }

    private void OnHideTimerTimeout()
    {
        Visible = false;
    }
}
