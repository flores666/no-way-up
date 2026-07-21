using System;
using LineZero.Core.Events;

namespace LineZero.Gameplay.Health;

internal readonly record struct HealthHealingPlan(
    HealthModel Health,
    int PreviousHealth,
    int RequestedAmount,
    int AppliedAmount,
    int CurrentHealth);

public sealed class HealthModel
{
    private bool _hasDied;
    private bool _acceptsDamage = true;

    public HealthModel(int maxHealth)
    {
        if (maxHealth < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxHealth),
                "Maximum health must be at least one.");
        }

        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
    }

    public int MaxHealth { get; }

    public int CurrentHealth { get; private set; }

    public bool IsAlive => CurrentHealth > 0;

    public bool IsDead => CurrentHealth == 0;

    public bool AcceptsDamage => _acceptsDamage;

    public event Action<HealthChangeResult>? Changed;

    public event Action<DamageInfo, HealthChangeResult>? Damaged;

    public event Action<HealthChangeResult>? Healed;

    public event Action<DamageInfo, HealthChangeResult>? Died;

    public HealthChangeResult ApplyDamage(DamageInfo damage)
    {
        ArgumentNullException.ThrowIfNull(damage);

        int previousHealth = CurrentHealth;
        if (IsDead || !_acceptsDamage)
        {
            return new HealthChangeResult(
                previousHealth,
                previousHealth,
                damage.Amount,
                0,
                causedDeath: false);
        }

        int appliedAmount = Math.Min(damage.Amount, CurrentHealth);
        CurrentHealth -= appliedAmount;
        bool causedDeath = CurrentHealth == 0 && !_hasDied;
        if (causedDeath)
        {
            _hasDied = true;
        }

        HealthChangeResult result = new(
            previousHealth,
            CurrentHealth,
            damage.Amount,
            appliedAmount,
            causedDeath);

        PublishDamage(damage, result);
        return result;
    }

    public bool DisableDamagePermanently()
    {
        if (!_acceptsDamage)
        {
            return false;
        }

        _acceptsDamage = false;
        return true;
    }

    public HealthChangeResult ApplyHealing(int amount)
    {
        ValidateHealingAmount(amount);
        if (!TryPrepareHealing(amount, out HealthHealingPlan plan))
        {
            return new HealthChangeResult(
                CurrentHealth,
                CurrentHealth,
                amount,
                0,
                causedDeath: false);
        }

        HealthChangeResult result = ApplyWithoutNotification(plan);
        PublishHealing(result);
        return result;
    }

    internal bool TryPrepareHealing(int amount, out HealthHealingPlan plan)
    {
        ValidateHealingAmount(amount);
        int previousHealth = CurrentHealth;
        if (IsDead || previousHealth == MaxHealth)
        {
            plan = default;
            return false;
        }

        int appliedAmount = Math.Min(amount, MaxHealth - previousHealth);
        plan = new HealthHealingPlan(
            this,
            previousHealth,
            amount,
            appliedAmount,
            previousHealth + appliedAmount);
        return true;
    }

    internal bool CanApply(HealthHealingPlan plan)
    {
        return ReferenceEquals(plan.Health, this) &&
               plan.PreviousHealth == CurrentHealth &&
               plan.PreviousHealth >= 0 &&
               plan.PreviousHealth < MaxHealth &&
               plan.RequestedAmount > 0 &&
               plan.AppliedAmount > 0 &&
               plan.CurrentHealth == plan.PreviousHealth + plan.AppliedAmount &&
               plan.CurrentHealth <= MaxHealth &&
               IsAlive;
    }

    internal HealthChangeResult ApplyWithoutNotification(HealthHealingPlan plan)
    {
        if (!CanApply(plan))
        {
            throw new InvalidOperationException(
                "The prepared health restoration is no longer valid.");
        }

        CurrentHealth = plan.CurrentHealth;
        return new HealthChangeResult(
            plan.PreviousHealth,
            plan.CurrentHealth,
            plan.RequestedAmount,
            plan.AppliedAmount,
            causedDeath: false);
    }

    internal void PublishHealing(HealthChangeResult result)
    {
        if (!result.Changed || result.CausedDeath)
        {
            throw new ArgumentException(
                "Healing publication requires a non-lethal completed health change.",
                nameof(result));
        }

        SafeEventPublisher.Publish(
            Changed,
            result,
            $"{nameof(HealthModel)}.{nameof(Changed)}");
        SafeEventPublisher.Publish(
            Healed,
            result,
            $"{nameof(HealthModel)}.{nameof(Healed)}");
    }

    private void PublishDamage(DamageInfo damage, HealthChangeResult result)
    {
        SafeEventPublisher.Publish(
            Changed,
            result,
            $"{nameof(HealthModel)}.{nameof(Changed)}");
        SafeEventPublisher.Publish(
            Damaged,
            damage,
            result,
            $"{nameof(HealthModel)}.{nameof(Damaged)}");
        if (result.CausedDeath)
        {
            SafeEventPublisher.Publish(
                Died,
                damage,
                result,
                $"{nameof(HealthModel)}.{nameof(Died)}");
        }
    }

    private static void ValidateHealingAmount(int amount)
    {
        if (amount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Healing amount must be at least one.");
        }
    }
}
