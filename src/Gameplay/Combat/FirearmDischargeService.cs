using System;
using LineZero.Gameplay.Health;

namespace LineZero.Gameplay.Combat;

public sealed class FirearmDischargeService
{
    private bool _isExecuting;

    public FirearmDischargeResult TryDischarge(
        FirearmState firearm,
        HealthModel? targetHealth = null,
        DamageInfo? damage = null)
    {
        ArgumentNullException.ThrowIfNull(firearm);
        if ((targetHealth is null) != (damage is null))
        {
            throw new ArgumentException(
                "Target health and damage must either both be supplied or both be omitted.");
        }

        if (_isExecuting)
        {
            return new FirearmDischargeResult(
                FirearmShotResult.Rejected(
                    FirearmShotStatus.CombatDisabled,
                    firearm.CurrentMagazineAmmo,
                    "A shot transaction is already in progress."),
                targetHealthChange: null);
        }

        if (!firearm.TryPrepareShot(
                out FirearmShotPlan firearmPlan,
                out FirearmShotResult rejection))
        {
            return new FirearmDischargeResult(rejection, targetHealthChange: null);
        }

        HealthDamagePlan damagePlan = default;
        bool hasDamagePlan = targetHealth is not null &&
                             damage is not null &&
                             targetHealth.TryPrepareDamage(
                                 damage,
                                 out damagePlan);

        _isExecuting = true;
        try
        {
            if (!firearm.CanApply(firearmPlan) ||
                (hasDamagePlan && !targetHealth!.CanApply(damagePlan)))
            {
                throw new InvalidOperationException(
                    "Firearm discharge plans became invalid before commit.");
            }

            FirearmShotResult shot = firearm.ApplyWithoutNotification(firearmPlan);
            HealthChangeResult? healthChange = hasDamagePlan
                ? targetHealth!.ApplyWithoutNotification(damagePlan)
                : null;

            firearm.PublishChanged();
            if (healthChange is not null)
            {
                targetHealth!.PublishDamage(damage!, healthChange);
            }

            return new FirearmDischargeResult(shot, healthChange);
        }
        finally
        {
            _isExecuting = false;
        }
    }
}
