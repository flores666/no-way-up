using System;
using Godot;

namespace LineZero.World3D;

public static class AimPlaneProjection3D
{
    private const float MinimumRayVerticalMagnitude = 0.000001f;
    private const float MinimumAimDistanceSquared = 0.0001f;

    public static bool TryIntersectHorizontalPlane(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        float planeHeight,
        out Vector3 aimPoint)
    {
        aimPoint = Vector3.Zero;
        if (!IsFinite(rayOrigin) ||
            !IsFinite(rayDirection) ||
            !float.IsFinite(planeHeight) ||
            MathF.Abs(rayDirection.Y) <= MinimumRayVerticalMagnitude)
        {
            return false;
        }

        float distanceAlongRay = (planeHeight - rayOrigin.Y) / rayDirection.Y;
        if (!float.IsFinite(distanceAlongRay) || distanceAlongRay < 0.0f)
        {
            return false;
        }

        Vector3 intersection = rayOrigin + (rayDirection * distanceAlongRay);
        if (!IsFinite(intersection))
        {
            return false;
        }

        intersection.Y = planeHeight;
        aimPoint = intersection;
        return true;
    }

    public static bool TryGetHorizontalDirection(
        Vector3 origin,
        Vector3 aimPoint,
        out Vector3 direction)
    {
        direction = Vector3.Zero;
        if (!IsFinite(origin) || !IsFinite(aimPoint))
        {
            return false;
        }

        Vector3 horizontal = aimPoint - origin;
        horizontal.Y = 0.0f;
        if (horizontal.LengthSquared() <= MinimumAimDistanceSquared)
        {
            return false;
        }

        direction = horizontal.Normalized();
        return IsFinite(direction);
    }

    public static bool TryGetYaw(
        Vector3 origin,
        Vector3 aimPoint,
        out float yaw)
    {
        yaw = 0.0f;
        if (!TryGetHorizontalDirection(origin, aimPoint, out Vector3 direction))
        {
            return false;
        }

        // Godot's conventional local forward direction is -Z.
        float candidate = MathF.Atan2(-direction.X, -direction.Z);
        if (!float.IsFinite(candidate))
        {
            return false;
        }

        yaw = candidate;
        return true;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
