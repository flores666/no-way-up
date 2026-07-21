using System.Threading.Tasks;
using LineZero.Gameplay.Health;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class HealthFeatureTests : IFeatureTestSuite
{
    public string Id => "health";

    public string Description => "Damage, healing, death, and terminal damage immunity";

    public Task RunAsync(FeatureTestContext context)
    {
        context.Run("damage-clamps-and-death-fires-once", () =>
        {
            HealthModel health = new(100);
            int changed = 0;
            int damaged = 0;
            int died = 0;
            health.Changed += _ => changed++;
            health.Damaged += (_, _) => damaged++;
            health.Died += (_, _) => died++;

            HealthChangeResult first = health.ApplyDamage(new DamageInfo(40));
            HealthChangeResult lethal = health.ApplyDamage(new DamageInfo(1000));
            HealthChangeResult duplicate = health.ApplyDamage(new DamageInfo(10));

            TestAssert.Equal(40, first.AppliedAmount, "First damage amount was incorrect.");
            TestAssert.Equal(60, first.CurrentHealth, "First damage health was incorrect.");
            TestAssert.True(lethal.CausedDeath, "Lethal damage did not report death.");
            TestAssert.Equal(0, health.CurrentHealth, "Health did not clamp to zero.");
            TestAssert.Equal(0, duplicate.AppliedAmount, "Dead actor accepted more damage.");
            TestAssert.Equal(2, changed, "Health change count was incorrect.");
            TestAssert.Equal(2, damaged, "Damage event count was incorrect.");
            TestAssert.Equal(1, died, "Death event was not exactly-once.");
        });

        context.Run("healing-clamps-to-maximum", () =>
        {
            HealthModel health = new(100);
            health.ApplyDamage(new DamageInfo(30));
            HealthChangeResult healing = health.ApplyHealing(100);

            TestAssert.Equal(30, healing.AppliedAmount, "Healing did not clamp to capacity.");
            TestAssert.Equal(100, health.CurrentHealth, "Healing did not restore maximum health.");
            TestAssert.False(health.ApplyHealing(10).Changed, "Full health changed on healing.");
        });

        context.Run("completion-damage-immunity-is-terminal", () =>
        {
            HealthModel health = new(100);
            int damageEvents = 0;
            health.Damaged += (_, _) => damageEvents++;

            TestAssert.True(
                health.DisableDamagePermanently(),
                "First terminal damage-disable call failed.");
            TestAssert.False(
                health.DisableDamagePermanently(),
                "Terminal damage-disable was not idempotent.");
            HealthChangeResult result = health.ApplyDamage(new DamageInfo(999));

            TestAssert.Equal(0, result.AppliedAmount, "Completed player accepted damage.");
            TestAssert.Equal(100, health.CurrentHealth, "Completed player health changed.");
            TestAssert.Equal(0, damageEvents, "Completed player published a damage event.");
        });


        context.Run("subscriber-failure-does-not-block-death", () =>
        {
            HealthModel health = new(25);
            int healthyChanged = 0;
            int damaged = 0;
            int died = 0;
            health.Changed += _ => throw new System.InvalidOperationException("Expected.");
            health.Changed += _ => healthyChanged++;
            health.Damaged += (_, _) => damaged++;
            health.Died += (_, _) => died++;

            HealthChangeResult result = health.ApplyDamage(new DamageInfo(25));

            TestAssert.True(result.CausedDeath, "Lethal damage did not complete.");
            TestAssert.Equal(1, healthyChanged,
                "A throwing Changed subscriber blocked a later subscriber.");
            TestAssert.Equal(1, damaged,
                "A throwing Changed subscriber blocked Damaged.");
            TestAssert.Equal(1, died,
                "A throwing Changed subscriber blocked Died.");
            TestAssert.Equal(0, health.CurrentHealth,
                "Subscriber failure repeated or reverted health mutation.");
        });

        return Task.CompletedTask;
    }
}
