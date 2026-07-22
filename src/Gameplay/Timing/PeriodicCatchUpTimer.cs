using System;

namespace LineZero.Gameplay.Timing;

public sealed class PeriodicCatchUpTimer
{
    private const double ComparisonEpsilonFactor = 1e-9;

    public PeriodicCatchUpTimer(
        double intervalSeconds,
        int maximumTicksPerAdvance)
    {
        if (!double.IsFinite(intervalSeconds) || intervalSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalSeconds),
                "Interval must be finite and positive.");
        }

        if (maximumTicksPerAdvance < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumTicksPerAdvance),
                "Catch-up limit must be at least one tick.");
        }

        IntervalSeconds = intervalSeconds;
        MaximumTicksPerAdvance = maximumTicksPerAdvance;
    }

    public double IntervalSeconds { get; }

    public int MaximumTicksPerAdvance { get; }

    public double AccumulatedSeconds { get; private set; }

    public PeriodicCatchUpResult Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0.0)
        {
            return new PeriodicCatchUpResult(0, AccumulatedSeconds);
        }

        double accumulated = AccumulatedSeconds + deltaSeconds;
        if (!double.IsFinite(accumulated))
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds),
                "Elapsed-time accumulation exceeded the finite range.");
        }

        AccumulatedSeconds = accumulated;
        double comparisonEpsilon = Math.Max(
            double.Epsilon,
            IntervalSeconds * ComparisonEpsilonFactor);
        int dueTicks = 0;
        while (dueTicks < MaximumTicksPerAdvance &&
               AccumulatedSeconds + comparisonEpsilon >= IntervalSeconds)
        {
            AccumulatedSeconds = Math.Max(
                0.0,
                AccumulatedSeconds - IntervalSeconds);
            dueTicks++;
        }

        // Whole intervals beyond the catch-up limit and the fractional remainder
        // stay in AccumulatedSeconds for subsequent bounded advances.
        return new PeriodicCatchUpResult(dueTicks, AccumulatedSeconds);
    }

    public void Clear()
    {
        AccumulatedSeconds = 0.0;
    }
}
