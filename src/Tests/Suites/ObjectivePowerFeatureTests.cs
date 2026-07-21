using System.Threading.Tasks;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Objectives;
using LineZero.Gameplay.Power;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class ObjectivePowerFeatureTests : IFeatureTestSuite
{
    public string Id => "objectives-power";

    public string Description => "Forward-only objectives, circuit activation, and fuse transaction";

    public Task RunAsync(FeatureTestContext context)
    {
        context.Run("objective-progression-is-forward-only-and-terminal", () =>
        {
            ObjectiveProgressModel objectives = new();
            int changes = 0;
            objectives.Changed += (_, _) => changes++;

            TestAssert.False(objectives.TryAdvanceTo(ObjectiveStage.OpenExit),
                "Objective skipped a required stage.");
            TestAssert.True(objectives.TryAdvanceTo(ObjectiveStage.RestorePower),
                "Objective could not advance to RestorePower.");
            TestAssert.False(objectives.TryAdvanceTo(ObjectiveStage.RestorePower),
                "Objective accepted a duplicate transition.");
            TestAssert.True(objectives.TryAdvanceTo(ObjectiveStage.OpenExit),
                "Objective could not advance to OpenExit.");
            TestAssert.True(objectives.TryAdvanceTo(ObjectiveStage.ReachExit),
                "Objective could not advance to ReachExit.");
            TestAssert.True(objectives.TryAdvanceTo(ObjectiveStage.Completed),
                "Objective could not complete.");
            TestAssert.False(objectives.TryAdvanceTo(ObjectiveStage.Completed),
                "Completed objective changed again.");
            TestAssert.Equal(4, changes, "Objective event count was incorrect.");
        });

        context.Run("missing-fuse-and-wrong-stage-change-nothing", () =>
        {
            FuseInstallationService service = new();
            InventoryModel inventory = new(capacity: 1);
            PowerCircuitModel circuit = new();
            ObjectiveProgressModel objectives = new();

            FuseInstallationResult wrongStage = service.TryInstall(
                inventory,
                circuit,
                objectives,
                canInstall: true);
            TestAssert.False(wrongStage.Success, "Fuse installed during FindFuse.");
            TestAssert.False(circuit.IsPowered, "Wrong-stage install powered the circuit.");

            objectives.TryAdvanceTo(ObjectiveStage.RestorePower);
            FuseInstallationResult missing = service.TryInstall(
                inventory,
                circuit,
                objectives,
                canInstall: true);
            TestAssert.False(missing.Success, "Missing-fuse installation succeeded.");
            TestAssert.False(circuit.IsPowered, "Missing-fuse installation powered circuit.");
        });

        context.Run("successful-fuse-install-is-exactly-once", () =>
        {
            InventoryModel inventory = new(capacity: 2);
            inventory.TryAdd(TestDataFactory.CreateReplacementFuse(), 1);
            PowerCircuitModel circuit = new();
            ObjectiveProgressModel objectives = new();
            objectives.TryAdvanceTo(ObjectiveStage.RestorePower);
            int inventoryEvents = 0;
            int circuitEvents = 0;
            int restoredEvents = 0;
            inventory.Changed += () => inventoryEvents++;
            circuit.Changed += () => circuitEvents++;
            circuit.PowerRestored += () =>
            {
                restoredEvents++;
                objectives.TryAdvanceTo(ObjectiveStage.OpenExit);
            };

            FuseInstallationService service = new();
            FuseInstallationResult success = service.TryInstall(
                inventory,
                circuit,
                objectives,
                canInstall: true);
            FuseInstallationResult duplicate = service.TryInstall(
                inventory,
                circuit,
                objectives,
                canInstall: true);

            TestAssert.True(success.Success && success.FuseConsumed && success.PowerRestored,
                "Valid fuse installation failed.");
            TestAssert.False(duplicate.Success || duplicate.FuseConsumed,
                "Duplicate fuse installation changed state.");
            TestAssert.Equal(0, inventory.CountByItemId("replacement_fuse"),
                "Fuse was not consumed exactly once.");
            TestAssert.True(circuit.HasInstalledFuse && circuit.IsPowered,
                "Circuit did not reach powered state.");
            TestAssert.Equal(ObjectiveStage.OpenExit, objectives.CurrentStage,
                "Power event did not advance objective.");
            TestAssert.Equal(1, inventoryEvents, "Inventory event count was incorrect.");
            TestAssert.Equal(1, circuitEvents, "Circuit event count was incorrect.");
            TestAssert.Equal(1, restoredEvents, "PowerRestored was not exactly-once.");
        });

        context.Run("subscriber-failure-preserves-critical-progression", () =>
        {
            InventoryModel inventory = new(capacity: 1);
            inventory.TryAdd(TestDataFactory.CreateReplacementFuse(), 1);
            PowerCircuitModel circuit = new();
            ObjectiveProgressModel objectives = new();
            objectives.TryAdvanceTo(ObjectiveStage.RestorePower);
            int criticalSubscriber = 0;
            circuit.PowerRestored += () => throw new System.InvalidOperationException("Expected.");
            circuit.PowerRestored += () =>
            {
                criticalSubscriber++;
                objectives.TryAdvanceTo(ObjectiveStage.OpenExit);
            };

            FuseInstallationResult result = new FuseInstallationService().TryInstall(
                inventory,
                circuit,
                objectives,
                canInstall: true);

            TestAssert.True(result.Success, "Subscriber failure changed installation result.");
            TestAssert.Equal(1, criticalSubscriber,
                "Failing subscriber blocked critical progression.");
            TestAssert.Equal(ObjectiveStage.OpenExit, objectives.CurrentStage,
                "Critical objective progression was not delivered.");
        });

        return Task.CompletedTask;
    }
}
