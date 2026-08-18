using System;
using Godot;

namespace LineZero.World2D.Presentation;

public sealed partial class PlayerCameraZoom2D : Camera2D
{
    [Export(PropertyHint.Range, "0.1,4.0,0.05,or_greater")]
    public float MinimumZoom { get; set; } = 0.5f;

    [Export(PropertyHint.Range, "0.1,6.0,0.05,or_greater")]
    public float MaximumZoom { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.05,1.0,0.05,or_greater")]
    public float ZoomStep { get; set; } = 0.25f;

    public override void _Ready()
    {
        ValidateSettings();

        float initialZoom = float.IsFinite(Zoom.X) && Zoom.X > 0.0f
            ? Zoom.X
            : 1.0f;
        ApplyZoom(Math.Clamp(initialZoom, MinimumZoom, MaximumZoom));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouseButton)
        {
            return;
        }

        int direction = mouseButton.ButtonIndex switch
        {
            MouseButton.WheelUp => 1,
            MouseButton.WheelDown => -1,
            _ => 0
        };

        if (direction == 0)
        {
            return;
        }

        float wheelFactor = float.IsFinite(mouseButton.Factor) && mouseButton.Factor > 0.0f
            ? mouseButton.Factor
            : 1.0f;
        float currentZoom = Zoom.X;
        float targetZoom = Math.Clamp(
            currentZoom + direction * ZoomStep * wheelFactor,
            MinimumZoom,
            MaximumZoom);

        if (!Mathf.IsEqualApprox(targetZoom, currentZoom))
        {
            ApplyZoom(targetZoom);
        }

        GetViewport().SetInputAsHandled();
    }

    private void ApplyZoom(float value)
    {
        Zoom = new Vector2(value, value);
    }

    private void ValidateSettings()
    {
        if (!float.IsFinite(MinimumZoom) || MinimumZoom <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerCameraZoom2D)} on '{Name}' requires a positive finite " +
                $"{nameof(MinimumZoom)}.");
        }

        if (!float.IsFinite(MaximumZoom) || MaximumZoom < MinimumZoom)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerCameraZoom2D)} on '{Name}' requires {nameof(MaximumZoom)} " +
                $"to be finite and greater than or equal to {nameof(MinimumZoom)}.");
        }

        if (!float.IsFinite(ZoomStep) || ZoomStep <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerCameraZoom2D)} on '{Name}' requires a positive finite " +
                $"{nameof(ZoomStep)}.");
        }
    }
}
