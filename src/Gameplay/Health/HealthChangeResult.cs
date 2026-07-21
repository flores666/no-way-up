using System;

namespace LineZero.Gameplay.Health;

public sealed class HealthChangeResult
{
    public HealthChangeResult(
        int previousHealth,
        int currentHealth,
        int requestedAmount,
        int appliedAmount,
        bool causedDeath)
    {
        if (previousHealth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(previousHealth),
                "Previous health cannot be negative.");
        }

        if (currentHealth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentHealth),
                "Current health cannot be negative.");
        }

        if (requestedAmount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedAmount),
                "Requested health-change amount must be at least one.");
        }

        if (appliedAmount < 0 || appliedAmount > requestedAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appliedAmount),
                "Applied amount must be within the requested amount.");
        }

        long healthDifference = Math.Abs((long)currentHealth - previousHealth);
        if (healthDifference != appliedAmount)
        {
            throw new ArgumentException(
                "Applied amount must equal the absolute health difference.",
                nameof(appliedAmount));
        }

        if (causedDeath && (previousHealth == 0 || currentHealth != 0 || appliedAmount == 0))
        {
            throw new ArgumentException(
                "A death-causing change must reduce positive health to zero.",
                nameof(causedDeath));
        }

        PreviousHealth = previousHealth;
        CurrentHealth = currentHealth;
        RequestedAmount = requestedAmount;
        AppliedAmount = appliedAmount;
        CausedDeath = causedDeath;
    }

    public int PreviousHealth { get; }

    public int CurrentHealth { get; }

    public int RequestedAmount { get; }

    public int AppliedAmount { get; }

    public bool Changed => AppliedAmount > 0;

    public bool CausedDeath { get; }
}
