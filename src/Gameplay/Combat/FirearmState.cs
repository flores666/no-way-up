using System;

namespace LineZero.Gameplay.Combat;

public sealed class FirearmState
{
    public FirearmState(FirearmDefinition definition, int initialMagazineAmmo)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();

        if (initialMagazineAmmo < 0 || initialMagazineAmmo > definition.MagazineCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialMagazineAmmo),
                $"Initial magazine ammunition must be between 0 and " +
                $"{definition.MagazineCapacity}.");
        }

        Definition = definition;
        CurrentMagazineAmmo = initialMagazineAmmo;
    }

    public FirearmDefinition Definition { get; }

    public int CurrentMagazineAmmo { get; private set; }

    public bool IsReloading { get; private set; }

    public bool HasMagazineAmmo => CurrentMagazineAmmo > 0;

    public bool CanFire => HasMagazineAmmo && !IsReloading;

    public int RoundsNeededToFillMagazine =>
        Definition.MagazineCapacity - CurrentMagazineAmmo;

    public event Action? Changed;

    public FirearmShotResult TryConsumeRound()
    {
        if (IsReloading)
        {
            return FirearmShotResult.Rejected(
                FirearmShotStatus.Reloading,
                CurrentMagazineAmmo,
                "Cannot fire while reloading.");
        }

        if (!HasMagazineAmmo)
        {
            return FirearmShotResult.Rejected(
                FirearmShotStatus.EmptyMagazine,
                CurrentMagazineAmmo,
                "Magazine empty.");
        }

        int magazineAmmoBefore = CurrentMagazineAmmo;
        CurrentMagazineAmmo--;
        FirearmShotResult result = FirearmShotResult.Fired(magazineAmmoBefore);
        Changed?.Invoke();
        return result;
    }

    public ReloadResult TryBeginReload(int availableReserveAmmo)
    {
        if (availableReserveAmmo < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableReserveAmmo),
                "Available reserve ammunition cannot be negative.");
        }

        if (IsReloading)
        {
            return ReloadResult.Rejected(
                ReloadStatus.AlreadyReloading,
                CurrentMagazineAmmo,
                "Reload already in progress.");
        }

        if (RoundsNeededToFillMagazine == 0)
        {
            return ReloadResult.Rejected(
                ReloadStatus.MagazineFull,
                CurrentMagazineAmmo,
                "Magazine already full.");
        }

        if (availableReserveAmmo == 0)
        {
            return ReloadResult.Rejected(
                ReloadStatus.NoReserveAmmo,
                CurrentMagazineAmmo,
                "No reserve ammunition.");
        }

        IsReloading = true;
        ReloadResult result = ReloadResult.Changed(
            ReloadStatus.Started,
            CurrentMagazineAmmo,
            "Reload started.");
        Changed?.Invoke();
        return result;
    }

    public ReloadResult CompleteReload(int suppliedRounds)
    {
        if (suppliedRounds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(suppliedRounds),
                "Supplied reload rounds cannot be negative.");
        }

        if (!IsReloading)
        {
            return ReloadResult.Rejected(
                ReloadStatus.NotReloading,
                CurrentMagazineAmmo,
                "No reload is in progress.");
        }

        int loadedRounds = Math.Min(suppliedRounds, RoundsNeededToFillMagazine);
        CurrentMagazineAmmo += loadedRounds;
        IsReloading = false;

        ReloadResult result = ReloadResult.Changed(
            ReloadStatus.Completed,
            CurrentMagazineAmmo,
            loadedRounds > 0
                ? $"Reloaded {loadedRounds} rounds."
                : "Reload completed without available ammunition.",
            suppliedRounds,
            loadedRounds);
        Changed?.Invoke();
        return result;
    }

    public ReloadResult CancelReload()
    {
        if (!IsReloading)
        {
            return ReloadResult.Rejected(
                ReloadStatus.NotReloading,
                CurrentMagazineAmmo,
                "No reload is in progress.");
        }

        IsReloading = false;
        ReloadResult result = ReloadResult.Changed(
            ReloadStatus.Canceled,
            CurrentMagazineAmmo,
            "Reload canceled.");
        Changed?.Invoke();
        return result;
    }
}
