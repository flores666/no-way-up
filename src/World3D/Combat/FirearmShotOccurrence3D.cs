using System;
using Godot;
using LineZero.Gameplay.Combat;

namespace LineZero.World3D.Combat;

public sealed class FirearmShotOccurrence3D
{
    public FirearmShotOccurrence3D(
        FirearmDischargeResult discharge,
        Vector3 safeNoiseOrigin,
        Vector3 muzzleOrigin,
        Vector3 rayEnd,
        Vector3 impactPoint,
        Node? hitCollider)
    {
        ArgumentNullException.ThrowIfNull(discharge);
        if (!discharge.Shot.Success)
        {
            throw new ArgumentException(
                "A 3D shot occurrence requires a completed discharge.",
                nameof(discharge));
        }

        ValidateFinite(safeNoiseOrigin, nameof(safeNoiseOrigin));
        ValidateFinite(muzzleOrigin, nameof(muzzleOrigin));
        ValidateFinite(rayEnd, nameof(rayEnd));
        ValidateFinite(impactPoint, nameof(impactPoint));
        if (hitCollider is not null && !GodotObject.IsInstanceValid(hitCollider))
        {
            throw new ArgumentException(
                "A supplied shot collider must be valid.",
                nameof(hitCollider));
        }

        Discharge = discharge;
        SafeNoiseOrigin = safeNoiseOrigin;
        MuzzleOrigin = muzzleOrigin;
        RayEnd = rayEnd;
        ImpactPoint = impactPoint;
        HitCollider = hitCollider;
    }

    public FirearmDischargeResult Discharge { get; }

    public Vector3 SafeNoiseOrigin { get; }

    public Vector3 MuzzleOrigin { get; }

    public Vector3 RayEnd { get; }

    public Vector3 ImpactPoint { get; }

    public Node? HitCollider { get; }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "3D shot positions must be finite.");
        }
    }
}
