using System;
using Godot;

namespace LineZero.World3D;

public static class GroundMovement3D
{
    private const float MinimumBasisLengthSquared = 0.000001f;

    public static Vector3 CalculateCameraRelativeDirection(
        Vector2 input,
        Vector3 cameraForward,
        Vector3 cameraRight,
        bool movementEnabled = true)
    {
        if (!movementEnabled || !IsFinite(input))
        {
            return Vector3.Zero;
        }

        if (input.LengthSquared() > 1.0f)
        {
            input = input.Normalized();
        }

        if (input.IsZeroApprox())
        {
            return Vector3.Zero;
        }

        Vector3 forward = FlattenAndNormalize(cameraForward);
        Vector3 right = FlattenAndNormalize(cameraRight);
        if (forward.IsZeroApprox() || right.IsZeroApprox())
        {
            return Vector3.Zero;
        }

        // Rebuild the right basis on the ground plane so skewed camera input
        // cannot make diagonal movement faster than axial movement.
        right -= forward * right.Dot(forward);
        if (right.LengthSquared() <= MinimumBasisLengthSquared)
        {
            right = forward.Cross(Vector3.Up);
        }

        right = right.Normalized();
        if (right.Dot(cameraRight) < 0.0f)
        {
            right = -right;
        }

        Vector3 direction = (right * input.X) + (forward * -input.Y);
        return direction.LengthSquared() > 1.0f
            ? direction.Normalized()
            : direction;
    }

    public static Vector3 CalculateTargetVelocity(
        Vector2 input,
        Vector3 cameraForward,
        Vector3 cameraRight,
        float walkingSpeed,
        bool movementEnabled = true)
    {
        if (!float.IsFinite(walkingSpeed) || walkingSpeed < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(walkingSpeed),
                "Walking speed must be finite and non-negative.");
        }

        return CalculateCameraRelativeDirection(
            input,
            cameraForward,
            cameraRight,
            movementEnabled) * walkingSpeed;
    }

    public static Vector3 MoveHorizontalVelocityToward(
        Vector3 currentVelocity,
        Vector3 targetVelocity,
        float maximumChange)
    {
        if (!IsFinite(currentVelocity) || !IsFinite(targetVelocity))
        {
            throw new ArgumentException(
                "Horizontal velocities must contain only finite values.");
        }

        if (!float.IsFinite(maximumChange) || maximumChange < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumChange),
                "Maximum velocity change must be finite and non-negative.");
        }

        Vector2 currentHorizontal = new(currentVelocity.X, currentVelocity.Z);
        Vector2 targetHorizontal = new(targetVelocity.X, targetVelocity.Z);
        Vector2 nextHorizontal = currentHorizontal.MoveToward(
            targetHorizontal,
            maximumChange);
        return new Vector3(
            nextHorizontal.X,
            currentVelocity.Y,
            nextHorizontal.Y);
    }

    private static Vector3 FlattenAndNormalize(Vector3 value)
    {
        if (!IsFinite(value))
        {
            return Vector3.Zero;
        }

        value.Y = 0.0f;
        return value.LengthSquared() > MinimumBasisLengthSquared
            ? value.Normalized()
            : Vector3.Zero;
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
