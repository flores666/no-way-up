using System;
using Godot;

namespace LineZero.UI;

public sealed partial class InteractionPromptController : MarginContainer
{
    private Label _promptLabel = null!;
    private string? _currentPrompt;

    public override void _Ready()
    {
        _promptLabel = GetNodeOrNull<Label>("%PromptLabel")
            ?? throw new InvalidOperationException(
                $"{nameof(InteractionPromptController)} on '{Name}' requires a PromptLabel.");

        Visible = false;
    }

    public void SetPrompt(string? prompt)
    {
        string? normalizedPrompt = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();
        if (string.Equals(_currentPrompt, normalizedPrompt, StringComparison.Ordinal))
        {
            return;
        }

        _currentPrompt = normalizedPrompt;
        if (normalizedPrompt is null)
        {
            _promptLabel.Text = string.Empty;
            Visible = false;
            return;
        }

        _promptLabel.Text = $"[E] {normalizedPrompt}";
        Visible = true;
    }
}

