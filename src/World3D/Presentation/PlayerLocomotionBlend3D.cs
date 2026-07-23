using System;
using Godot;

namespace LineZero.World3D.Presentation;

public readonly record struct PlayerLocomotionBlendResult(
    Vector2 LocalBlend,
    float HorizontalSpeed,
    float SpeedRatio,
    bool IsMoving);

public static class PlayerLocomotionBlend3D
{
    public static PlayerLocomotionBlendResult Calculate(
        Vector3 actualVelocity,
        Vector3 aimForward,
        Vector3 aimRight,
        float maximumModeSpeed,
        float idleStopSpeed,
        float idleStartSpeed,
        bool wasMoving)
    {
        ValidateInputs(
            actualVelocity,
            aimForward,
            aimRight,
            maximumModeSpeed,
            idleStopSpeed,
            idleStartSpeed);

        Vector3 horizontalVelocity = new(
            actualVelocity.X,
            0.0f,
            actualVelocity.Z);
        Vector3 horizontalForward = new(
            aimForward.X,
            0.0f,
            aimForward.Z);
        Vector3 horizontalRight = new(
            aimRight.X,
            0.0f,
            aimRight.Z);
        horizontalForward = horizontalForward.Normalized();
        horizontalRight = horizontalRight.Normalized();

        float speed = horizontalVelocity.Length();
        bool isMoving = wasMoving
            ? speed > idleStopSpeed
            : speed >= idleStartSpeed;
        if (!isMoving)
        {
            return new PlayerLocomotionBlendResult(
                Vector2.Zero,
                speed,
                0.0f,
                IsMoving: false);
        }

        Vector2 localVelocity = new(
            horizontalVelocity.Dot(horizontalRight),
            horizontalVelocity.Dot(horizontalForward));
        float speedRatio = Mathf.Clamp(speed / maximumModeSpeed, 0.0f, 1.5f);
        Vector2 localBlend = (localVelocity / maximumModeSpeed).LimitLength(1.0f);
        return new PlayerLocomotionBlendResult(
            localBlend,
            speed,
            speedRatio,
            IsMoving: true);
    }

    private static void ValidateInputs(
        Vector3 actualVelocity,
        Vector3 aimForward,
        Vector3 aimRight,
        float maximumModeSpeed,
        float idleStopSpeed,
        float idleStartSpeed)
    {
        if (!actualVelocity.IsFinite() ||
            !aimForward.IsFinite() ||
            !aimRight.IsFinite() ||
            new Vector2(aimForward.X, aimForward.Z).LengthSquared() < 0.0001f ||
            new Vector2(aimRight.X, aimRight.Z).LengthSquared() < 0.0001f)
        {
            throw new ArgumentException(
                "Locomotion vectors must be finite with non-zero XZ facing axes.");
        }

        if (!float.IsFinite(maximumModeSpeed) || maximumModeSpeed <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumModeSpeed));
        }

        if (!float.IsFinite(idleStopSpeed) ||
            !float.IsFinite(idleStartSpeed) ||
            idleStopSpeed < 0.0f ||
            idleStartSpeed <= idleStopSpeed ||
            idleStartSpeed > maximumModeSpeed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleStartSpeed),
                "Idle thresholds must satisfy 0 <= stop < start <= mode speed.");
        }
    }
}
