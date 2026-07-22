using System;
using LineZero.Gameplay.Health;

namespace LineZero.Gameplay.Combat;

public sealed class FirearmDischargeResult
{
    public FirearmDischargeResult(
        FirearmShotResult shot,
        HealthChangeResult? targetHealthChange)
    {
        ArgumentNullException.ThrowIfNull(shot);
        if (!shot.Success && targetHealthChange is not null)
        {
            throw new ArgumentException(
                "A rejected shot cannot include target damage.",
                nameof(targetHealthChange));
        }

        Shot = shot;
        TargetHealthChange = targetHealthChange;
    }

    public FirearmShotResult Shot { get; }

    public HealthChangeResult? TargetHealthChange { get; }

    public bool DamageApplied => TargetHealthChange?.Changed == true;
}
