using System.Globalization;
using System.Threading.Tasks;
using Godot;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Objectives;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;
using LineZero.UI;

namespace LineZero.Tests.Suites;

public sealed class HudFeatureTests : IFeatureTestSuite
{
    public string Id => "hud";

    public string Description => "Event-driven stamina, flashlight, and objective displays";

    public async Task RunAsync(FeatureTestContext context)
    {
        await context.RunAsync("stamina-text-does-not-cross-gameplay-thresholds", async () =>
        {
            await AssertStaminaDisplayAsync(
                context,
                consumedAmount: 90.01,
                expectedDisplayedCurrent: 9.9,
                expectedActualCurrent: 9.99,
                failureMessage: "Stamina text falsely crossed the sprint threshold.");

            await AssertStaminaDisplayAsync(
                context,
                consumedAmount: 90.0,
                expectedDisplayedCurrent: 10.0,
                expectedActualCurrent: 10.0,
                failureMessage: "Stamina threshold was not displayed when actually reached.");

            await AssertStaminaDisplayAsync(
                context,
                consumedAmount: 0.01,
                expectedDisplayedCurrent: 99.9,
                expectedActualCurrent: 99.99,
                failureMessage: "Stamina text falsely displayed maximum.");
        });

        await context.RunAsync("flashlight-hud-shows-power-and-warning", async () =>
        {
            FlashlightHudController hud = context.InstantiateScene<FlashlightHudController>(
                "res://scenes/ui/FlashlightHud.tscn");
            await context.WaitProcessFramesAsync();
            FlashlightModel flashlight = new(
                TestDataFactory.CreateFlashlightDefinition(),
                startOn: true);
            InventoryModel inventory = new(capacity: 1);
            inventory.TryAdd(TestDataFactory.CreateBattery(), 1);
            hud.Bind(flashlight, inventory);

            flashlight.Drain(80.0);
            Label status = hud.GetNode<Label>("%FlashlightStatusLabel");
            Label charge = hud.GetNode<Label>("%FlashlightChargeLabel");
            ProgressBar bar = hud.GetNode<ProgressBar>("%FlashlightChargeBar");
            TestAssert.Equal("ON · LOW", status.Text,
                "Low-charge HUD lost the ON state.");
            TestAssert.Equal(
                FormatCharge(20.0, 100.0),
                charge.Text,
                "Flashlight charge text was misleading.");
            TestAssert.NearlyEqual(20.0, bar.Value, 1e-9,
                "Flashlight bar did not use actual charge.");

            flashlight.TurnOff();
            TestAssert.Equal("OFF · LOW", status.Text,
                "Turning off low-charge flashlight did not change HUD state.");
            hud.SetActorAlive(false);
            TestAssert.Equal("DEAD", status.Text,
                "Dead flashlight HUD did not enter terminal state.");

            await context.DisposeNodeAsync(hud);
        });

        await context.RunAsync("objective-hud-shows-only-current-stage", async () =>
        {
            ObjectiveHudController hud = context.InstantiateScene<ObjectiveHudController>(
                "res://scenes/ui/ObjectiveHud.tscn");
            await context.WaitProcessFramesAsync();
            ObjectiveProgressModel objectives = new();
            hud.Bind(objectives);
            Label objective = hud.GetNode<Label>("%ObjectiveLabel");
            Label status = hud.GetNode<Label>("%ObjectiveStatusLabel");

            TestAssert.Equal("FIND A REPLACEMENT FUSE", objective.Text,
                "Objective HUD did not show initial stage.");
            objectives.TryAdvanceTo(ObjectiveStage.RestorePower);
            TestAssert.Equal("RESTORE POWER AT THE MAINTENANCE PANEL", objective.Text,
                "Objective HUD did not update through events.");
            objectives.TryAdvanceTo(ObjectiveStage.OpenExit);
            objectives.TryAdvanceTo(ObjectiveStage.ReachExit);
            objectives.TryAdvanceTo(ObjectiveStage.Completed);
            TestAssert.Equal("ESCAPE COMPLETE", objective.Text,
                "Objective HUD did not show completion.");
            TestAssert.Equal("COMPLETED", status.Text,
                "Objective HUD completion status was incorrect.");

            await context.DisposeNodeAsync(hud);
        });
    }

    private static async Task AssertStaminaDisplayAsync(
        FeatureTestContext context,
        double consumedAmount,
        double expectedDisplayedCurrent,
        double expectedActualCurrent,
        string failureMessage)
    {
        StaminaHudController hud = context.InstantiateScene<StaminaHudController>(
            "res://scenes/ui/StaminaHud.tscn");
        await context.WaitProcessFramesAsync();

        StaminaModel stamina = new(100.0);
        TestMovementModeSource movement = new();
        HealthModel health = new(100);
        hud.Bind(stamina, movement, health, minimumStaminaToStartSprint: 10.0);
        stamina.Consume(consumedAmount);

        Label label = hud.GetNode<Label>("%StaminaLabel");
        ProgressBar bar = hud.GetNode<ProgressBar>("%StaminaBar");
        TestAssert.Equal(
            FormatStamina(expectedDisplayedCurrent, 100.0),
            label.Text,
            failureMessage);
        TestAssert.NearlyEqual(
            expectedActualCurrent,
            bar.Value,
            1e-9,
            "Stamina progress bar did not use the actual value.");

        await context.DisposeNodeAsync(hud);
    }

    private static string FormatStamina(double current, double maximum)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            "STAMINA {0:0.0} / {1:0.0}",
            current,
            maximum);
    }

    private static string FormatCharge(double current, double maximum)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            "{0:0.0} / {1:0.0}",
            current,
            maximum);
    }
}
