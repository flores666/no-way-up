using System;
using Godot;

namespace LineZero.UI;

public sealed partial class EscapeCompletePanelController : CenterContainer
{
    private Button _quitButton = null!;

    public override void _Ready()
    {
        _quitButton = GetNodeOrNull<Button>("%QuitButton")
            ?? throw new InvalidOperationException(
                $"{nameof(EscapeCompletePanelController)} on '{Name}' requires a QuitButton.");
        _quitButton.Pressed += OnQuitPressed;
        Visible = false;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_quitButton))
        {
            _quitButton.Pressed -= OnQuitPressed;
        }
    }

    public void ShowCompletion()
    {
        Visible = true;
        _quitButton.GrabFocus();
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}
