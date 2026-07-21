using System;

namespace LineZero.Gameplay.Combat;

public enum FirearmShotStatus
{
    Fired,
    EmptyMagazine,
    Reloading,
    FireInterval,
    MuzzleObstructed,
    CombatDisabled,
    OwnerDead,
}

public sealed class FirearmShotResult
{
    private FirearmShotResult(
        FirearmShotStatus status,
        int magazineAmmoBefore,
        int magazineAmmoAfter,
        string message)
    {
        if (magazineAmmoBefore < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(magazineAmmoBefore),
                "Magazine ammunition before a shot cannot be negative.");
        }

        if (magazineAmmoAfter < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(magazineAmmoAfter),
                "Magazine ammunition after a shot cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A firearm-shot result requires a message.",
                nameof(message));
        }

        bool fired = status == FirearmShotStatus.Fired;
        if (fired && magazineAmmoBefore != magazineAmmoAfter + 1)
        {
            throw new ArgumentException(
                "A fired shot must consume exactly one magazine round.",
                nameof(magazineAmmoAfter));
        }

        if (!fired && magazineAmmoBefore != magazineAmmoAfter)
        {
            throw new ArgumentException(
                "A rejected shot cannot change magazine ammunition.",
                nameof(magazineAmmoAfter));
        }

        Status = status;
        MagazineAmmoBefore = magazineAmmoBefore;
        MagazineAmmoAfter = magazineAmmoAfter;
        Message = message.Trim();
    }

    public FirearmShotStatus Status { get; }

    public bool Success => Status == FirearmShotStatus.Fired;

    public bool RoundConsumed => Success;

    public int MagazineAmmoBefore { get; }

    public int MagazineAmmoAfter { get; }

    public string Message { get; }

    public static FirearmShotResult Fired(int magazineAmmoBefore)
    {
        if (magazineAmmoBefore < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(magazineAmmoBefore),
                "A fired shot requires at least one magazine round.");
        }

        return new FirearmShotResult(
            FirearmShotStatus.Fired,
            magazineAmmoBefore,
            magazineAmmoBefore - 1,
            "Shot fired.");
    }

    public static FirearmShotResult Rejected(
        FirearmShotStatus status,
        int currentMagazineAmmo,
        string message)
    {
        if (status == FirearmShotStatus.Fired)
        {
            throw new ArgumentException(
                "Use the fired result factory for a successful shot.",
                nameof(status));
        }

        return new FirearmShotResult(
            status,
            currentMagazineAmmo,
            currentMagazineAmmo,
            message);
    }
}
