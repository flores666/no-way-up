using System;
using Godot;

namespace LineZero.Gameplay.Health;

public sealed class DamageInfo
{
    public DamageInfo(int amount, Node? source = null, string? damageKind = null)
    {
        if (amount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Damage amount must be at least one.");
        }

        if (damageKind is not null && string.IsNullOrWhiteSpace(damageKind))
        {
            throw new ArgumentException(
                "Damage kind must be non-empty when supplied.",
                nameof(damageKind));
        }

        Amount = amount;
        Source = source;
        DamageKind = damageKind?.Trim();
    }

    public int Amount { get; }

    public Node? Source { get; }

    public string? DamageKind { get; }
}
