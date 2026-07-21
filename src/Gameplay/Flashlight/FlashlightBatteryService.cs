using System;
using LineZero.Gameplay.Inventory;

namespace LineZero.Gameplay.Flashlight;

public sealed class FlashlightBatteryService
{
    public const string ReplacementSucceededMessage = "Battery replaced.";
    public const string AlreadyFullMessage = "Flashlight battery is already full.";
    public const string NoSpareBatteriesMessage = "No spare batteries.";
    public const string CannotReplaceNowMessage = "Cannot replace battery now.";

    private bool _isReplacing;

    public BatteryReplacementResult TryReplaceBattery(
        FlashlightModel flashlight,
        InventoryModel inventory,
        bool canReplace)
    {
        ArgumentNullException.ThrowIfNull(flashlight);
        ArgumentNullException.ThrowIfNull(inventory);

        double previousCharge = flashlight.CurrentCharge;
        if (!canReplace || _isReplacing)
        {
            return Rejected(previousCharge, CannotReplaceNowMessage);
        }

        _isReplacing = true;
        try
        {
            BatteryReplacementResult? rejection = ValidatePreconditions(
                flashlight,
                inventory,
                previousCharge);
            if (rejection.HasValue)
            {
                return rejection.Value;
            }

            string batteryItemId = FlashlightDefinition.RequiredBatteryItemId;
            if (!inventory.TryPrepareSingleItemRemoval(
                    batteryItemId,
                    out InventorySingleItemRemovalPlan removalPlan) ||
                !flashlight.TryPrepareChargeRestoration(
                    flashlight.ChargeRestoredPerBattery,
                    out FlashlightChargeRestorationPlan restorationPlan) ||
                !inventory.CanApply(removalPlan) ||
                !flashlight.CanApply(restorationPlan))
            {
                return Rejected(previousCharge, CannotReplaceNowMessage);
            }

            // Both prepared mutations are revalidated immediately before this
            // synchronous commit. No callback, await, or public notification can
            // run between the inventory removal and flashlight restoration.
            inventory.ApplyWithoutNotification(removalPlan);
            FlashlightChargeResult chargeResult =
                flashlight.ApplyWithoutNotification(restorationPlan);

            // Both models are already consistent before observers run. Safe event
            // publication isolates failing HUD subscribers and continues delivery.
            inventory.PublishChanged();
            flashlight.PublishChanged(chargeResult);

            return new BatteryReplacementResult(
                success: true,
                batteryConsumed: true,
                previousCharge,
                chargeResult.CurrentCharge,
                chargeResult.Applied,
                ReplacementSucceededMessage);
        }
        finally
        {
            _isReplacing = false;
        }
    }

    private static BatteryReplacementResult? ValidatePreconditions(
        FlashlightModel flashlight,
        InventoryModel inventory,
        double previousCharge)
    {
        if (flashlight.IsEffectivelyFull ||
            flashlight.CalculateRestorableCharge(
                flashlight.ChargeRestoredPerBattery) <=
            flashlight.FullChargeEpsilon)
        {
            return Rejected(previousCharge, AlreadyFullMessage);
        }

        if (inventory.CountByItemId(FlashlightDefinition.RequiredBatteryItemId) < 1)
        {
            return Rejected(previousCharge, NoSpareBatteriesMessage);
        }

        return null;
    }

    private static BatteryReplacementResult Rejected(
        double currentCharge,
        string message)
    {
        return new BatteryReplacementResult(
            success: false,
            batteryConsumed: false,
            currentCharge,
            currentCharge,
            restoredCharge: 0.0,
            message);
    }
}
