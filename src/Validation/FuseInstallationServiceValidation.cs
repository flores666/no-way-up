using System;
using Godot;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;
using LineZero.Gameplay.Objectives;
using LineZero.Gameplay.Power;

namespace LineZero.Validation;

public sealed partial class FuseInstallationServiceValidation : Node
{
    public override void _Ready()
    {
        try
        {
            ValidateMissingFuse();
            ValidateDuplicateInstallation();
            ValidateSubscriberFailureIsolation();
            GD.Print("FuseInstallationService validation passed.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"FuseInstallationService validation failed: {exception}");
            GetTree().Quit(1);
        }
    }

    private static void ValidateMissingFuse()
    {
        InventoryModel inventory = new(capacity: 2);
        PowerCircuitModel circuit = new();
        ObjectiveProgressModel objectives = CreateRestorePowerObjectives();
        FuseInstallationService service = new();
        int inventoryNotifications = 0;
        int powerNotifications = 0;

        inventory.Changed += () => inventoryNotifications++;
        circuit.PowerRestored += () => powerNotifications++;

        FuseInstallationResult result = service.TryInstall(
            inventory,
            circuit,
            objectives,
            canInstall: true);

        Require(!result.Success, "Missing-fuse installation must fail.");
        Require(!result.FuseConsumed, "Missing-fuse installation consumed an item.");
        Require(!result.PowerRestored, "Missing-fuse installation restored power.");
        Require(result.Message == FuseInstallationService.FuseRequiredMessage,
            "Missing-fuse installation returned the wrong message.");
        Require(inventory.CountByItemId(FuseInstallationService.ReplacementFuseItemId) == 0,
            "Missing-fuse validation changed inventory.");
        Require(!circuit.HasInstalledFuse && !circuit.IsPowered,
            "Missing-fuse validation changed circuit state.");
        Require(inventoryNotifications == 0 && powerNotifications == 0,
            "Missing-fuse validation published notifications.");
    }

    private static void ValidateDuplicateInstallation()
    {
        InventoryModel inventory = new(capacity: 2);
        inventory.TryAdd(CreateReplacementFuse(), quantity: 2);
        PowerCircuitModel circuit = new();
        ObjectiveProgressModel objectives = CreateRestorePowerObjectives();
        FuseInstallationService service = new();
        int inventoryNotifications = 0;
        int changedNotifications = 0;
        int restoredNotifications = 0;

        inventory.Changed += () => inventoryNotifications++;
        circuit.Changed += () => changedNotifications++;
        circuit.PowerRestored += () =>
        {
            restoredNotifications++;
            objectives.TryAdvanceTo(ObjectiveStage.OpenExit);
        };

        FuseInstallationResult first = service.TryInstall(
            inventory,
            circuit,
            objectives,
            canInstall: true);
        FuseInstallationResult duplicate = service.TryInstall(
            inventory,
            circuit,
            objectives,
            canInstall: true);

        Require(first.Success && first.FuseConsumed && first.PowerRestored,
            "The first valid installation did not succeed atomically.");
        Require(!duplicate.Success && !duplicate.FuseConsumed && !duplicate.PowerRestored,
            "Duplicate installation changed state.");
        Require(duplicate.Message == FuseInstallationService.PowerOnlineMessage,
            "Duplicate installation returned the wrong message.");
        Require(inventory.CountByItemId(FuseInstallationService.ReplacementFuseItemId) == 1,
            "Duplicate installation consumed more than one fuse.");
        Require(circuit.HasInstalledFuse && circuit.IsPowered,
            "Successful installation did not power the circuit.");
        Require(inventoryNotifications == 1 &&
                changedNotifications == 1 &&
                restoredNotifications == 1,
            "Successful installation notifications were not exactly-once.");
        Require(objectives.CurrentStage == ObjectiveStage.OpenExit,
            "Power restoration did not advance the objective.");
    }

    private static void ValidateSubscriberFailureIsolation()
    {
        InventoryModel inventory = new(capacity: 1);
        inventory.TryAdd(CreateReplacementFuse(), quantity: 1);
        PowerCircuitModel circuit = new();
        ObjectiveProgressModel objectives = CreateRestorePowerObjectives();
        FuseInstallationService service = new();
        int healthyInventorySubscribers = 0;
        int healthyCircuitSubscribers = 0;
        int criticalPowerSubscribers = 0;
        int healthyObjectiveSubscribers = 0;

        inventory.Changed += ThrowExpectedSubscriberFailure;
        inventory.Changed += () => healthyInventorySubscribers++;
        circuit.Changed += ThrowExpectedSubscriberFailure;
        circuit.Changed += () => healthyCircuitSubscribers++;
        circuit.PowerRestored += ThrowExpectedSubscriberFailure;
        circuit.PowerRestored += () =>
        {
            criticalPowerSubscribers++;
            Require(objectives.TryAdvanceTo(ObjectiveStage.OpenExit),
                "Critical progression subscriber could not advance the objective.");
        };
        objectives.Changed += (_, _) => ThrowExpectedSubscriberFailure();
        objectives.Changed += (_, _) => healthyObjectiveSubscribers++;

        FuseInstallationResult result = service.TryInstall(
            inventory,
            circuit,
            objectives,
            canInstall: true);

        Require(result.Success && result.FuseConsumed && result.PowerRestored,
            "Subscriber failure changed the successful transaction result.");
        Require(inventory.CountByItemId(FuseInstallationService.ReplacementFuseItemId) == 0,
            "Subscriber failure prevented exact fuse consumption.");
        Require(circuit.HasInstalledFuse && circuit.IsPowered,
            "Subscriber failure prevented circuit activation.");
        Require(objectives.CurrentStage == ObjectiveStage.OpenExit,
            "Subscriber failure prevented critical power progression.");
        Require(healthyInventorySubscribers == 1 &&
                healthyCircuitSubscribers == 1 &&
                criticalPowerSubscribers == 1 &&
                healthyObjectiveSubscribers == 1,
            "One failing subscriber stopped another required notification.");
    }

    private static ObjectiveProgressModel CreateRestorePowerObjectives()
    {
        ObjectiveProgressModel objectives = new();
        Require(objectives.TryAdvanceTo(ObjectiveStage.RestorePower),
            "Validation setup could not reach RestorePower.");
        return objectives;
    }

    private static ItemDefinition CreateReplacementFuse()
    {
        return new ItemDefinition
        {
            Id = FuseInstallationService.ReplacementFuseItemId,
            DisplayName = "Replacement Fuse",
            Description = "Validation fuse.",
            MaxStackSize = 1,
        };
    }

    private static void ThrowExpectedSubscriberFailure()
    {
        throw new InvalidOperationException(
            "Expected validation subscriber failure.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
