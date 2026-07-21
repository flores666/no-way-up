using System.Threading.Tasks;
using Godot;
using LineZero.Core;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Items;
using LineZero.Gameplay.Power;
using LineZero.Tests.Framework;
using LineZero.UI;
using LineZero.World2D;
using LineZero.World2D.Combat;
using LineZero.World2D.Interaction;
using LineZero.World2D.Levels;

namespace LineZero.Tests.Suites;

public sealed class PrototypeFlowFeatureTests : IFeatureTestSuite
{
    public string Id => "prototype-flow";

    public string Description =>
        "Complete fuse-to-exit loop and terminal gameplay-state integration";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("full-objective-loop-reaches-terminal-completion", async () =>
        {
            Main main = context.InstantiateScene<Main>("res://scenes/main/Main.tscn");
            await context.WaitProcessFramesAsync(3);
            await context.WaitPhysicsFramesAsync(2);
            TestAssert.True(main.IsInitialized,
                "Main did not initialize before the prototype-flow test.");

            PlayerController2D player = main.GetNode<PlayerController2D>("%Player");
            TestLevelController2D level = main.GetNode<TestLevelController2D>("%TestLevel");
            ObjectiveHudController objectiveHud = main.GetNode<ObjectiveHudController>(
                "%ObjectiveHud");
            EscapeCompletePanelController completionPanel =
                main.GetNode<EscapeCompletePanelController>("%EscapeCompletePanel");
            PlayerInteractor2D interactor = player.GetNode<PlayerInteractor2D>(
                "%PlayerInteractor2D");
            PlayerWeaponController2D weapon = player.GetNode<PlayerWeaponController2D>(
                "%PlayerWeaponController2D");
            PlayerFlashlightController2D flashlight =
                player.GetNode<PlayerFlashlightController2D>(
                    "%PlayerFlashlightController2D");
            InventoryPanelController inventoryPanel =
                main.GetNode<InventoryPanelController>("%InventoryPanel");
            Label objectiveLabel = objectiveHud.GetNode<Label>("%ObjectiveLabel");

            ItemDefinition fuse = ResourceLoader.Load<ItemDefinition>(
                "res://data/items/ReplacementFuse.tres")
                ?? throw new TestAssertionException(
                    "Could not load the replacement-fuse resource.");
            player.Inventory.TryAdd(fuse, 1);
            TestAssert.Equal(
                "RESTORE POWER AT THE MAINTENANCE PANEL",
                objectiveLabel.Text,
                "Adding the fuse did not advance FindFuse to RestorePower.");

            InteractionResult installation = level.FuseBox.Interact(
                new InteractionContext(player));
            TestAssert.Equal(
                FuseInstallationService.SuccessMessage,
                installation.Message,
                "Valid fuse-box interaction failed.");
            TestAssert.True(level.PowerCircuit.Model.IsPowered,
                "Fuse-box interaction did not power the circuit.");
            TestAssert.Equal(0, player.Inventory.CountByItemId("replacement_fuse"),
                "Prototype flow did not consume exactly one replacement fuse.");
            TestAssert.Equal(
                "OPEN THE EMERGENCY EXIT",
                objectiveLabel.Text,
                "Power restoration did not advance the objective to OpenExit.");

            player.GlobalPosition = level.ExitZone.GlobalPosition;
            await context.WaitPhysicsFramesAsync(3);
            TestAssert.False(completionPanel.Visible,
                "Entering the exit zone before ReachExit completed the prototype early.");

            level.EmergencyExitDoor.Interact(new InteractionContext(player));
            TestAssert.True(level.EmergencyExitDoor.IsOpening,
                "Powered emergency door rejected a valid interaction.");
            TestAssert.Equal(
                "OPEN THE EMERGENCY EXIT",
                objectiveLabel.Text,
                "Objective advanced while the emergency door was still moving.");

            await context.WaitSecondsAsync(level.EmergencyExitDoor.AnimationDuration + 0.25);
            await context.WaitProcessFramesAsync(2);

            TestAssert.True(level.EmergencyExitDoor.IsOpen,
                "Emergency door did not reach its terminal open state.");
            TestAssert.True(completionPanel.Visible,
                "Early exit-zone overlap did not show the completion panel.");
            TestAssert.Equal("ESCAPE COMPLETE", objectiveLabel.Text,
                "Full prototype flow did not reach Completed.");
            TestAssert.False(player.Health.AcceptsDamage,
                "Completed player health still accepted damage.");
            int healthBefore = player.Health.CurrentHealth;
            player.Health.ApplyDamage(new DamageInfo(999, main, "test"));
            TestAssert.Equal(healthBefore, player.Health.CurrentHealth,
                "Completed player transitioned toward death after completion.");

            TestAssert.False(player.IsGameplayInputEnabled,
                "Completion did not disable movement and posture input.");
            TestAssert.False(interactor.IsGameplayInputEnabled,
                "Completion did not disable world interaction.");
            TestAssert.False(weapon.IsCombatInputEnabled,
                "Completion did not disable combat input.");
            TestAssert.False(flashlight.IsFlashlightInputEnabled,
                "Completion did not disable flashlight input.");
            TestAssert.False(flashlight.Model.IsOn,
                "Completion did not turn off the flashlight.");
            TestAssert.False(inventoryPanel.IsToggleEnabled,
                "Completion did not disable inventory reopening.");

            await context.DisposeNodeAsync(main);
        });
    }
}
