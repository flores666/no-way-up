using System;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Objectives;

namespace LineZero.Gameplay.Power;

public sealed class FuseInstallationService
{
    public const string ReplacementFuseItemId = "replacement_fuse";
    public const string SuccessMessage = "Power restored.";
    public const string FuseRequiredMessage = "A replacement fuse is required.";
    public const string PowerOnlineMessage = "Power is online.";
    public const string CannotInstallMessage = "Cannot install the fuse now.";

    private bool _isInstalling;

    public FuseInstallationResult TryInstall(
        InventoryModel inventory,
        PowerCircuitModel circuit,
        ObjectiveProgressModel objectives,
        bool canInstall)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(objectives);

        if (!canInstall || _isInstalling)
        {
            return Rejected(CannotInstallMessage);
        }

        _isInstalling = true;
        try
        {
            FuseInstallationResult? rejection = ValidatePreconditions(
                inventory,
                circuit,
                objectives);
            if (rejection.HasValue)
            {
                return rejection.Value;
            }

            if (!inventory.TryPrepareSingleItemRemoval(
                    ReplacementFuseItemId,
                    out InventorySingleItemRemovalPlan removalPlan) ||
                !circuit.TryPrepareInstallation(
                    out PowerCircuitInstallationPlan installationPlan) ||
                objectives.CurrentStage != ObjectiveStage.RestorePower ||
                !inventory.CanApply(removalPlan) ||
                !circuit.CanApply(installationPlan))
            {
                return Rejected(CannotInstallMessage);
            }

            // The final checks and both commits run synchronously without callbacks,
            // awaits, or public notifications between them. Expected failures have
            // already returned, so the prepared mutations form one logical commit.
            inventory.ApplyWithoutNotification(removalPlan);
            circuit.ApplyWithoutNotification(installationPlan);

            // Both mutations are complete before any external code can observe them.
            // Safe publication isolates each subscriber, so UI failures cannot prevent
            // PowerRestored from reaching the progression subscriber.
            inventory.PublishChanged();
            circuit.PublishInstallationCompleted();

            return new FuseInstallationResult(
                Success: true,
                FuseConsumed: true,
                PowerRestored: true,
                Message: SuccessMessage);
        }
        finally
        {
            _isInstalling = false;
        }
    }

    private static FuseInstallationResult? ValidatePreconditions(
        InventoryModel inventory,
        PowerCircuitModel circuit,
        ObjectiveProgressModel objectives)
    {
        if (!circuit.CanInstallFuse)
        {
            return Rejected(PowerOnlineMessage);
        }

        if (objectives.CurrentStage != ObjectiveStage.RestorePower)
        {
            return Rejected(CannotInstallMessage);
        }

        if (inventory.CountByItemId(ReplacementFuseItemId) < 1)
        {
            return Rejected(FuseRequiredMessage);
        }

        return null;
    }

    private static FuseInstallationResult Rejected(string message)
    {
        return new FuseInstallationResult(
            Success: false,
            FuseConsumed: false,
            PowerRestored: false,
            Message: message);
    }
}
