using System;
using Godot;

namespace LineZero.Gameplay.Health;

public sealed partial class HealthComponent : Node
{
    private HealthModel? _health;

    [Export(PropertyHint.Range, "1,9999,1,or_greater")]
    public int MaxHealth { get; set; } = 100;

    public HealthModel Health => _health
        ?? throw new InvalidOperationException(
            $"{nameof(HealthComponent)} on '{Name}' has not been initialized.");

    public override void _Ready()
    {
        if (MaxHealth < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(HealthComponent)} on '{Name}' requires positive maximum health.");
        }

        _health = new HealthModel(MaxHealth);
    }
}
