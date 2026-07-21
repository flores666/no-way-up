using System;

namespace LineZero.Gameplay.Health;

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

        Changed?.Invoke(result);
        Damaged?.Invoke(damage, result);
        if (causedDeath)
        {
            Died?.Invoke(damage, result);
        }

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
        if (amount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Healing amount must be at least one.");
        }

        int previousHealth = CurrentHealth;
        if (IsDead || CurrentHealth == MaxHealth)
        {
            return new HealthChangeResult(
                previousHealth,
                previousHealth,
                amount,
                0,
                causedDeath: false);
        }

        int appliedAmount = Math.Min(amount, MaxHealth - CurrentHealth);
        CurrentHealth += appliedAmount;

        HealthChangeResult result = new(
            previousHealth,
            CurrentHealth,
            amount,
            appliedAmount,
            causedDeath: false);

        Changed?.Invoke(result);
        Healed?.Invoke(result);
        return result;
    }
}
