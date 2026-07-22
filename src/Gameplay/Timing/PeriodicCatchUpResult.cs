namespace LineZero.Gameplay.Timing;

public readonly record struct PeriodicCatchUpResult(
    int DueTicks,
    double RemainingDebtSeconds);
