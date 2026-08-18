using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private readonly int[] _spareMagazineRounds;
    private readonly ReadOnlyCollection<int> _readOnlySpareMagazineRounds;

    public FirearmState(
        FirearmDefinition definition,
        int initialMagazineAmmo,
        int initialFullSpareMagazineCount = 0)
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

        if (initialFullSpareMagazineCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialFullSpareMagazineCount),
                "Initial spare magazine count cannot be negative.");
        }

        if (definition.ReloadMechanism != FirearmReloadMechanism.DetachableMagazine &&
            initialFullSpareMagazineCount != 0)
        {
            throw new ArgumentException(
                "Only detachable-magazine firearms can own spare magazines.",
                nameof(initialFullSpareMagazineCount));
        }

        Definition = definition;
        CurrentMagazineAmmo = initialMagazineAmmo;
        _spareMagazineRounds = new int[initialFullSpareMagazineCount];
        Array.Fill(_spareMagazineRounds, definition.MagazineCapacity);
        _readOnlySpareMagazineRounds = Array.AsReadOnly(_spareMagazineRounds);
    }

    public FirearmDefinition Definition { get; }

    public int CurrentMagazineAmmo { get; private set; }

    public bool IsReloading { get; private set; }

    public bool HasMagazineAmmo => CurrentMagazineAmmo > 0;

    public bool CanFire => HasMagazineAmmo && !IsReloading;

    public int RoundsNeededToFillMagazine =>
        Definition.MagazineCapacity - CurrentMagazineAmmo;

    public IReadOnlyList<int> SpareMagazineRounds => _readOnlySpareMagazineRounds;

    public int SpareMagazineCount => _spareMagazineRounds.Length;

    public int UsableSpareMagazineCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < _spareMagazineRounds.Length; index++)
            {
                if (_spareMagazineRounds[index] > 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int TotalMagazineAmmo
    {
        get
        {
            int total = CurrentMagazineAmmo;
            for (int index = 0; index < _spareMagazineRounds.Length; index++)
            {
                total = checked(total + _spareMagazineRounds[index]);
            }

            return total;
        }
    }

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
        if (Definition.ReloadMechanism != FirearmReloadMechanism.LooseRounds)
        {
            throw new InvalidOperationException(
                "Loose-round reload cannot be used by a detachable-magazine firearm.");
        }

        if (availableReserveAmmo < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableReserveAmmo),
                "Available reserve ammunition cannot be negative.");
        }

        ReloadResult? commonRejection = TryGetCommonReloadRejection();
        if (commonRejection is not null)
        {
            return commonRejection;
        }

        if (availableReserveAmmo == 0)
        {
            return ReloadResult.Rejected(
                ReloadStatus.NoReserveAmmo,
                CurrentMagazineAmmo,
                "No reserve ammunition.");
        }

        return BeginReload();
    }

    public ReloadResult TryBeginMagazineReload()
    {
        if (Definition.ReloadMechanism != FirearmReloadMechanism.DetachableMagazine)
        {
            throw new InvalidOperationException(
                "Magazine-swap reload can only be used by detachable-magazine firearms.");
        }

        ReloadResult? commonRejection = TryGetCommonReloadRejection();
        if (commonRejection is not null)
        {
            return commonRejection;
        }

        if (FindBestReplacementMagazineIndex() < 0)
        {
            return ReloadResult.Rejected(
                ReloadStatus.NoUsableMagazine,
                CurrentMagazineAmmo,
                "No spare magazine contains more ammunition than the current magazine.");
        }

        return BeginReload();
    }

    public ReloadResult CompleteReload(int suppliedRounds)
    {
        if (Definition.ReloadMechanism != FirearmReloadMechanism.LooseRounds)
        {
            throw new InvalidOperationException(
                "Loose-round reload cannot be completed by a detachable-magazine firearm.");
        }

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

    public ReloadResult CompleteMagazineReload()
    {
        if (Definition.ReloadMechanism != FirearmReloadMechanism.DetachableMagazine)
        {
            throw new InvalidOperationException(
                "Magazine-swap reload can only be completed by detachable-magazine firearms.");
        }

        if (!IsReloading)
        {
            return ReloadResult.Rejected(
                ReloadStatus.NotReloading,
                CurrentMagazineAmmo,
                "No reload is in progress.");
        }

        int replacementIndex = FindBestReplacementMagazineIndex();
        if (replacementIndex < 0)
        {
            return ReloadResult.Rejected(
                ReloadStatus.NoUsableMagazine,
                CurrentMagazineAmmo,
                "No usable spare magazine is available.");
        }

        int previousMagazineAmmo = CurrentMagazineAmmo;
        int replacementMagazineAmmo = _spareMagazineRounds[replacementIndex];

        // The removed magazine is retained in the same pouch slot. This preserves
        // partially used and empty magazines instead of converting them to loose ammo.
        _spareMagazineRounds[replacementIndex] = previousMagazineAmmo;
        CurrentMagazineAmmo = replacementMagazineAmmo;
        IsReloading = false;

        ReloadResult result = ReloadResult.Changed(
            ReloadStatus.Completed,
            CurrentMagazineAmmo,
            $"Magazine swapped: {replacementMagazineAmmo} rounds loaded.");
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
        if (Definition.ReloadMechanism != FirearmReloadMechanism.LooseRounds)
        {
            throw new InvalidOperationException(
                "Loose-round reload plans are invalid for detachable-magazine firearms.");
        }

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
        return Definition.ReloadMechanism == FirearmReloadMechanism.LooseRounds &&
               ReferenceEquals(plan.Firearm, this) &&
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

    private ReloadResult? TryGetCommonReloadRejection()
    {
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

        return null;
    }

    private ReloadResult BeginReload()
    {
        IsReloading = true;
        ReloadResult result = ReloadResult.Changed(
            ReloadStatus.Started,
            CurrentMagazineAmmo,
            "Reload started.");
        PublishChanged();
        return result;
    }

    private int FindBestReplacementMagazineIndex()
    {
        int bestIndex = -1;
        int bestRounds = CurrentMagazineAmmo;

        for (int index = 0; index < _spareMagazineRounds.Length; index++)
        {
            int rounds = _spareMagazineRounds[index];
            if (rounds <= bestRounds)
            {
                continue;
            }

            bestRounds = rounds;
            bestIndex = index;
        }

        return bestIndex;
    }
}
