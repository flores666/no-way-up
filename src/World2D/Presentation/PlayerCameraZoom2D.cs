using System;
using Godot;

namespace LineZero.World2D.Presentation;

public sealed partial class PlayerCameraZoom2D : Camera2D
{
    private const float MinimumTeleportSnapDistance = 1.0f;

    private Node2D _followTarget = null!;
    private Vector2 _previousPhysicsPosition;
    private Vector2 _currentPhysicsPosition;
    private bool _hasPhysicsSample;

    [Export(PropertyHint.Range, "0.1,4.0,0.05,or_greater")]
    public float MinimumZoom { get; set; } = 0.5f;

    [Export(PropertyHint.Range, "0.1,6.0,0.05,or_greater")]
    public float MaximumZoom { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.05,1.0,0.05,or_greater")]
    public float ZoomStep { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "1.0,4096.0,1.0,or_greater")]
    public float TeleportSnapDistance { get; set; } = 128.0f;

    public override void _Ready()
    {
        ValidateSettings();

        _followTarget = GetParent() as Node2D
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerCameraZoom2D)} on '{Name}' requires a Node2D parent.");

        Vector2 initialTargetPosition = _followTarget.GlobalPosition;
        if (!IsFinite(initialTargetPosition))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerCameraZoom2D)} on '{Name}' received a non-finite initial target position.");
        }

        _previousPhysicsPosition = initialTargetPosition;
        _currentPhysicsPosition = initialTargetPosition;
        _hasPhysicsSample = true;
        GlobalPosition = initialTargetPosition;

        float initialZoom = float.IsFinite(Zoom.X) && Zoom.X > 0.0f
            ? Zoom.X
            : 1.0f;
        ApplyZoom(Math.Clamp(initialZoom, MinimumZoom, MaximumZoom));
        ForceUpdateScroll();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!GodotObject.IsInstanceValid(_followTarget))
        {
            return;
        }

        Vector2 targetPosition = _followTarget.GlobalPosition;
        if (!IsFinite(targetPosition))
        {
            return;
        }

        if (!_hasPhysicsSample)
        {
            _previousPhysicsPosition = targetPosition;
            _currentPhysicsPosition = targetPosition;
            _hasPhysicsSample = true;
            return;
        }

        float teleportDistanceSquared = TeleportSnapDistance * TeleportSnapDistance;
        if (_currentPhysicsPosition.DistanceSquaredTo(targetPosition) > teleportDistanceSquared)
        {
            _previousPhysicsPosition = targetPosition;
            _currentPhysicsPosition = targetPosition;
            GlobalPosition = targetPosition;
            ForceUpdateScroll();
            return;
        }

        _previousPhysicsPosition = _currentPhysicsPosition;
        _currentPhysicsPosition = targetPosition;
    }

    public override void _Process(double delta)
    {
        if (!_hasPhysicsSample)
        {
            return;
        }

        float interpolationFraction = Math.Clamp(
            (float)Engine.GetPhysicsInterpolationFraction(),
            0.0f,
            1.0f);

        Vector2 renderedTargetPosition = _previousPhysicsPosition.Lerp(
            _currentPhysicsPosition,
            interpolationFraction);
        if (!IsFinite(renderedTargetPosition))
        {
            return;
        }

        // The camera must use exactly the same interpolated position as the player.
        // Rounding the camera independently creates a fractional player-to-camera
        // offset on the moving axis, which makes nearest-filtered pixel art look blurry.
        // Pixel snapping is handled by the renderer; do not quantize the camera here.
        GlobalPosition = renderedTargetPosition;

        // Camera2D performs its own internal canvas update. Force it after assigning
        // the render-frame position so the camera never trails the player by one frame.
        ForceUpdateScroll();
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
            ForceUpdateScroll();
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

        if (!float.IsFinite(TeleportSnapDistance) ||
            TeleportSnapDistance < MinimumTeleportSnapDistance)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerCameraZoom2D)} on '{Name}' requires {nameof(TeleportSnapDistance)} " +
                $"to be finite and at least {MinimumTeleportSnapDistance}.");
        }
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
