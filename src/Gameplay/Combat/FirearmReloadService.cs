using System;
using LineZero.Gameplay.Inventory;

namespace LineZero.Gameplay.Combat;

public sealed class FirearmReloadService
{
    private bool _isExecuting;

    public ReloadResult TryCompleteReload(
        FirearmState firearm,
        InventoryModel inventory,
        string ammoItemId)
    {
        ArgumentNullException.ThrowIfNull(firearm);
        ArgumentNullException.ThrowIfNull(inventory);
        if (string.IsNullOrWhiteSpace(ammoItemId))
        {
            throw new ArgumentException("Ammo item ID must be non-empty.", nameof(ammoItemId));
        }

        if (_isExecuting)
        {
            return ReloadResult.Rejected(
                ReloadStatus.AlreadyReloading,
                firearm.CurrentMagazineAmmo,
                "Reload transaction is already in progress.");
        }

        _isExecuting = true;
        try
        {
            int roundsNeeded = firearm.RoundsNeededToFillMagazine;
            if (roundsNeeded == 0)
            {
                return ReloadResult.Rejected(
                    ReloadStatus.MagazineFull,
                    firearm.CurrentMagazineAmmo,
                    "Magazine already full.");
            }

            int availableReserveAmmo = inventory.CountByItemId(ammoItemId);
            if (availableReserveAmmo == 0)
            {
                return ReloadResult.Rejected(
                    ReloadStatus.NoReserveAmmo,
                    firearm.CurrentMagazineAmmo,
                    "No reserve ammunition.");
            }

            if (!firearm.IsReloading)
            {
                return ReloadResult.Rejected(
                    ReloadStatus.NotReloading,
                    firearm.CurrentMagazineAmmo,
                    "No reload is in progress.");
            }

            int loadAmount = Math.Min(roundsNeeded, availableReserveAmmo);
            if (!inventory.TryPrepareItemRemoval(
                    ammoItemId,
                    loadAmount,
                    out InventoryItemQuantityRemovalPlan? inventoryPlan) ||
                inventoryPlan is null)
            {
                return ReloadResult.Rejected(
                    ReloadStatus.NoReserveAmmo,
                    firearm.CurrentMagazineAmmo,
                    "No reserve ammunition.");
            }

            if (!firearm.TryPrepareReloadCompletion(
                    loadAmount,
                    out FirearmReloadCompletionPlan firearmPlan,
                    out ReloadResult rejection))
            {
                return rejection;
            }

            if (!inventory.CanApply(inventoryPlan) || !firearm.CanApply(firearmPlan))
            {
                throw new InvalidOperationException(
                    "Reload transaction plans became invalid before commit.");
            }

            InventoryItemRemovalResult removal =
                inventory.ApplyWithoutNotification(inventoryPlan);
            ReloadResult result = firearm.ApplyWithoutNotification(firearmPlan);
            if (removal.RemovedQuantity != result.LoadedRounds ||
                result.LoadedRounds != loadAmount)
            {
                throw new InvalidOperationException(
                    "Reload transaction did not conserve ammunition.");
            }

            inventory.PublishChanged();
            firearm.PublishChanged();
            return result;
        }
        finally
        {
            _isExecuting = false;
        }
    }
}
