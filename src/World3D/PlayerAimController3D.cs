using System;
using Godot;

namespace LineZero.World3D;

public sealed partial class PlayerAimController3D : Node
{
    private CharacterBody3D? _player;
    private Node3D? _visualPivot;
    private Camera3D? _camera;
    private Node3D? _aimPointMarker;
    private Viewport? _viewport;
    private bool _isAimEnabled = true;

    [Export]
    public float AimPlaneHeight { get; set; }

    [Export(PropertyHint.Range, "1.0,500.0,1.0")]
    public float MaximumAimDistance { get; set; } = 250.0f;

    public bool HasValidAimPoint { get; private set; }

    public Vector3 AimPoint { get; private set; }

    public override void _Ready()
    {
        if (!float.IsFinite(AimPlaneHeight))
        {
            throw new InvalidOperationException(
                $"{nameof(AimPlaneHeight)} must be finite.");
        }

        if (!float.IsFinite(MaximumAimDistance) ||
            MaximumAimDistance < 1.0f ||
            MaximumAimDistance > 500.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(MaximumAimDistance)} must be between 1 and 500.");
        }

        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (_player is null || _visualPivot is null)
        {
            throw new InvalidOperationException("Aim actor binding is missing.");
        }

        Camera3D camera = _camera
            ?? throw new InvalidOperationException("Aim camera binding is missing.");
        Viewport viewport = _viewport
            ?? throw new InvalidOperationException("Aim viewport binding is missing.");

        if (!_isAimEnabled)
        {
            SetAimInvalid();
            return;
        }

        Vector2 mousePosition = viewport.GetMousePosition();
        Vector3 rayOrigin = camera.ProjectRayOrigin(mousePosition);
        Vector3 rayDirection = camera.ProjectRayNormal(mousePosition);
        if (!AimPlaneProjection3D.TryIntersectHorizontalPlane(
                rayOrigin,
                rayDirection,
                AimPlaneHeight,
                out Vector3 aimPoint) ||
            !TryApplyWorldAimPoint(aimPoint))
        {
            SetAimInvalid();
        }
    }

    public void Bind(
        CharacterBody3D player,
        Node3D visualPivot,
        Camera3D camera,
        Node3D aimPointMarker)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(visualPivot);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(aimPointMarker);

        if (_player is not null &&
            (!ReferenceEquals(_player, player) ||
             !ReferenceEquals(_visualPivot, visualPivot) ||
             !ReferenceEquals(_camera, camera) ||
             !ReferenceEquals(_aimPointMarker, aimPointMarker)))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerAimController3D)} on '{Name}' is already bound.");
        }

        _player = player;
        _visualPivot = visualPivot;
        _camera = camera;
        _aimPointMarker = aimPointMarker;
        _viewport = GetViewport();
        _aimPointMarker.Visible = false;
        SetProcess(true);
    }

    public void SetAimEnabled(bool enabled)
    {
        _isAimEnabled = enabled;
        if (!enabled)
        {
            SetAimInvalid();
        }
    }

    public bool TryGetAimDirection(out Vector3 direction)
    {
        CharacterBody3D? player = _player;
        if (!_isAimEnabled || !HasValidAimPoint || player is null)
        {
            direction = Vector3.Zero;
            return false;
        }

        return AimPlaneProjection3D.TryGetHorizontalDirection(
            player.GlobalPosition,
            AimPoint,
            out direction);
    }

    public bool TryApplyWorldAimPoint(Vector3 aimPoint)
    {
        CharacterBody3D? player = _player;
        Node3D? visualPivot = _visualPivot;
        if (!_isAimEnabled ||
            player is null ||
            visualPivot is null ||
            !float.IsFinite(aimPoint.X) ||
            !float.IsFinite(aimPoint.Y) ||
            !float.IsFinite(aimPoint.Z) ||
            player.GlobalPosition.DistanceSquaredTo(aimPoint) >
                MaximumAimDistance * MaximumAimDistance ||
            !AimPlaneProjection3D.TryGetYaw(
                player.GlobalPosition,
                aimPoint,
                out float yaw))
        {
            return false;
        }

        Vector3 rotation = visualPivot.GlobalRotation;
        rotation.X = 0.0f;
        rotation.Y = yaw;
        rotation.Z = 0.0f;
        visualPivot.GlobalRotation = rotation;
        AimPoint = aimPoint;
        HasValidAimPoint = true;
        if (_aimPointMarker is not null)
        {
            _aimPointMarker.GlobalPosition = aimPoint + (Vector3.Up * 0.03f);
            _aimPointMarker.Visible = true;
        }

        return true;
    }

    private void SetAimInvalid()
    {
        HasValidAimPoint = false;
        if (_aimPointMarker is not null)
        {
            _aimPointMarker.Visible = false;
        }
    }
}
