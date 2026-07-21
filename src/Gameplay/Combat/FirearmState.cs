using System;
using LineZero.Core.Events;

namespace LineZero.Gameplay.Combat;

internal readonly record struct FirearmReloadCompletionPlan(
    FirearmState Firearm,
    int MagazineAmmoBefore,
    int SuppliedRounds,
    int LoadedRounds,
    int MagazineAmmoAfter);

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
        PublishChanged();
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
        PublishChanged();
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

        if (!TryPrepareReloadCompletion(
                suppliedRounds,
                out FirearmReloadCompletionPlan plan,
                out ReloadResult rejection))
        {
            return rejection;
        }

        ReloadResult result = ApplyWithoutNotification(plan);
        PublishChanged();
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
        PublishChanged();
        return result;
    }

    internal bool TryPrepareReloadCompletion(
        int availableReserveAmmo,
        out FirearmReloadCompletionPlan plan,
        out ReloadResult rejection)
    {
        if (availableReserveAmmo < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableReserveAmmo),
                "Available reserve ammunition cannot be negative.");
        }

        if (RoundsNeededToFillMagazine == 0)
        {
            plan = default;
            rejection = ReloadResult.Rejected(
                ReloadStatus.MagazineFull,
                CurrentMagazineAmmo,
                "Magazine already full.");
            return false;
        }

        if (availableReserveAmmo == 0)
        {
            plan = default;
            rejection = ReloadResult.Rejected(
                ReloadStatus.NoReserveAmmo,
                CurrentMagazineAmmo,
                "No reserve ammunition.");
            return false;
        }

        if (!IsReloading)
        {
            plan = default;
            rejection = ReloadResult.Rejected(
                ReloadStatus.NotReloading,
                CurrentMagazineAmmo,
                "No reload is in progress.");
            return false;
        }

        int loadedRounds = Math.Min(availableReserveAmmo, RoundsNeededToFillMagazine);
        plan = new FirearmReloadCompletionPlan(
            this,
            CurrentMagazineAmmo,
            availableReserveAmmo,
            loadedRounds,
            CurrentMagazineAmmo + loadedRounds);
        rejection = null!;
        return true;
    }

    internal bool CanApply(FirearmReloadCompletionPlan plan)
    {
        return ReferenceEquals(plan.Firearm, this) &&
               IsReloading &&
               CurrentMagazineAmmo == plan.MagazineAmmoBefore &&
               plan.SuppliedRounds > 0 &&
               plan.LoadedRounds > 0 &&
               plan.LoadedRounds <= plan.SuppliedRounds &&
               plan.MagazineAmmoAfter ==
                   plan.MagazineAmmoBefore + plan.LoadedRounds &&
               plan.MagazineAmmoAfter <= Definition.MagazineCapacity;
    }

    internal ReloadResult ApplyWithoutNotification(FirearmReloadCompletionPlan plan)
    {
        if (!CanApply(plan))
        {
            throw new InvalidOperationException(
                "The prepared firearm reload is no longer valid.");
        }

        CurrentMagazineAmmo = plan.MagazineAmmoAfter;
        IsReloading = false;
        return ReloadResult.Changed(
            ReloadStatus.Completed,
            CurrentMagazineAmmo,
            $"Reloaded {plan.LoadedRounds} rounds.",
            plan.SuppliedRounds,
            plan.LoadedRounds);
    }

    internal void PublishChanged()
    {
        SafeEventPublisher.Publish(
            Changed,
            $"{nameof(FirearmState)}.{nameof(Changed)}");
    }
}
