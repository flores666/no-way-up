namespace LineZero.Gameplay.Noise;

public readonly record struct FootstepCadenceAdvanceResult(
    long CompletedSteps,
    long PendingSteps,
    double CycleProgress);
