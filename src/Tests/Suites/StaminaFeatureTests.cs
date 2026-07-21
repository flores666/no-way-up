using System;
using System.Threading.Tasks;
using LineZero.Gameplay.Movement;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class StaminaFeatureTests : IFeatureTestSuite
{
    public string Id => "stamina";

    public string Description => "Stamina drain, recovery, depletion, and validation";

    public Task RunAsync(FeatureTestContext context)
    {
        context.Run("consume-and-restore-preserve-bounds", () =>
        {
            StaminaModel stamina = new(100.0);
            StaminaChangeResult consumed = stamina.Consume(150.0);
            StaminaChangeResult restored = stamina.Restore(150.0);

            TestAssert.NearlyEqual(100.0, consumed.Applied, 1e-9, "Consume did not clamp.");
            TestAssert.NearlyEqual(0.0, consumed.Current, 1e-9, "Consume did not reach zero.");
            TestAssert.NearlyEqual(100.0, restored.Applied, 1e-9, "Restore did not clamp.");
            TestAssert.NearlyEqual(100.0, stamina.Current, 1e-9, "Restore exceeded maximum.");
        });

        context.Run("threshold-events-are-transition-based", () =>
        {
            StaminaModel stamina = new(20.0);
            int depleted = 0;
            int recovered = 0;
            stamina.Depleted += _ => depleted++;
            stamina.RecoveredFromEmpty += _ => recovered++;

            stamina.Consume(20.0);
            stamina.Consume(1.0);
            stamina.Restore(1.0);
            stamina.Restore(1.0);

            TestAssert.Equal(1, depleted, "Depleted event was not exactly-once.");
            TestAssert.Equal(1, recovered, "Recovered event was not crossing-based.");
        });

        context.Run("invalid-changes-are-rejected", () =>
        {
            StaminaModel stamina = new(100.0);
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => stamina.Consume(0.0),
                "Zero stamina consumption was accepted.");
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => stamina.Restore(double.NaN),
                "NaN stamina restoration was accepted.");
        });


        context.Run("subscriber-failure-does-not-block-critical-events", () =>
        {
            StaminaModel stamina = new(10.0);
            int healthyChanged = 0;
            int depleted = 0;
            int recovered = 0;
            stamina.Changed += _ => throw new InvalidOperationException("Expected.");
            stamina.Changed += _ => healthyChanged++;
            stamina.Depleted += _ => depleted++;
            stamina.RecoveredFromEmpty += _ => recovered++;

            stamina.Consume(10.0);
            stamina.Restore(1.0);

            TestAssert.Equal(2, healthyChanged,
                "A throwing stamina subscriber blocked later Changed handlers.");
            TestAssert.Equal(1, depleted,
                "A throwing Changed subscriber blocked Depleted.");
            TestAssert.Equal(1, recovered,
                "A throwing Changed subscriber blocked RecoveredFromEmpty.");
            TestAssert.NearlyEqual(1.0, stamina.Current, 1e-9,
                "Subscriber failure repeated or reverted stamina mutation.");
        });

        return Task.CompletedTask;
    }
}
