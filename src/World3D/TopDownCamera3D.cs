using System;
using Godot;

namespace LineZero.World3D;

public sealed partial class TopDownCamera3D : Camera3D
{
    private const int CameraProcessPriority = -10;

    private Node3D? _target;

    [Export(PropertyHint.Range, "-180.0,180.0,0.5")]
    public float YawDegrees { get; set; } = 45.0f;

    [Export(PropertyHint.Range, "25.0,80.0,0.5")]
    public float PitchDegrees { get; set; } = 55.0f;

    [Export(PropertyHint.Range, "5.0,80.0,0.5")]
    public float CameraDistance { get; set; } = 24.0f;

    [Export(PropertyHint.Range, "5.0,80.0,0.5")]
    public float OrthographicSize { get; set; } = 22.0f;

    [Export(PropertyHint.Range, "0.0,5.0,0.1")]
    public float TargetHeightOffset { get; set; } = 0.9f;

    [Export(PropertyHint.Range, "0.1,30.0,0.1")]
    public float FollowSmoothing { get; set; } = 8.0f;

    [Export]
    public bool SmoothingEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0.01,5.0,0.01")]
    public float NearClipDistance { get; set; } = 0.1f;

    [Export(PropertyHint.Range, "10.0,1000.0,1.0")]
    public float FarClipDistance { get; set; } = 120.0f;

    public Vector3 FixedRotationDegrees =>
        new(-PitchDegrees, YawDegrees, 0.0f);

    public override void _Ready()
    {
        ValidateConfiguration();
        Projection = ProjectionType.Orthogonal;
        Size = OrthographicSize;
        Near = NearClipDistance;
        Far = FarClipDistance;
        RotationDegrees = FixedRotationDegrees;
        ProcessPriority = CameraProcessPriority;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        Node3D target = _target
            ?? throw new InvalidOperationException(
                $"{nameof(TopDownCamera3D)} on '{Name}' has no follow target.");
        Vector3 desiredPosition = CalculateDesiredPosition(target.GlobalPosition);
        float frameSeconds = float.IsFinite((float)delta) && delta > 0.0
            ? (float)delta
            : 0.0f;
        float blend = SmoothingEnabled
            ? 1.0f - MathF.Exp(-FollowSmoothing * frameSeconds)
            : 1.0f;
        GlobalPosition = GlobalPosition.Lerp(desiredPosition, blend);
        RotationDegrees = FixedRotationDegrees;
    }

    public void BindTarget(Node3D target, bool snapImmediately = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_target is not null && !ReferenceEquals(_target, target))
        {
            throw new InvalidOperationException(
                $"{nameof(TopDownCamera3D)} on '{Name}' is already bound " +
                "to a different target.");
        }

        _target = target;
        RotationDegrees = FixedRotationDegrees;
        if (snapImmediately)
        {
            GlobalPosition = CalculateDesiredPosition(target.GlobalPosition);
        }

        SetProcess(true);
    }

    private Vector3 CalculateDesiredPosition(Vector3 targetPosition)
    {
        Vector3 focus = targetPosition + (Vector3.Up * TargetHeightOffset);
        Vector3 viewDirection = -GlobalTransform.Basis.Z.Normalized();
        return focus - (viewDirection * CameraDistance);
    }

    private void ValidateConfiguration()
    {
        ValidateRange(YawDegrees, -180.0f, 180.0f, nameof(YawDegrees));
        ValidateRange(PitchDegrees, 25.0f, 80.0f, nameof(PitchDegrees));
        ValidateRange(CameraDistance, 5.0f, 80.0f, nameof(CameraDistance));
        ValidateRange(OrthographicSize, 5.0f, 80.0f, nameof(OrthographicSize));
        ValidateRange(TargetHeightOffset, 0.0f, 5.0f, nameof(TargetHeightOffset));
        ValidateRange(FollowSmoothing, 0.1f, 30.0f, nameof(FollowSmoothing));
        ValidateRange(NearClipDistance, 0.01f, 5.0f, nameof(NearClipDistance));
        if (!float.IsFinite(FarClipDistance) ||
            FarClipDistance < 10.0f ||
            FarClipDistance > 1000.0f ||
            FarClipDistance <= NearClipDistance)
        {
            throw new InvalidOperationException(
                $"{nameof(FarClipDistance)} must be between 10 and 1000 and " +
                $"greater than {nameof(NearClipDistance)}.");
        }
    }

    private static void ValidateRange(
        float value,
        float minimum,
        float maximum,
        string propertyName)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{propertyName} must be between {minimum} and {maximum}.");
        }
    }
}
