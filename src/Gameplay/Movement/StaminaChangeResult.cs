using System;

namespace LineZero.Gameplay.Movement;

public readonly struct StaminaChangeResult
{
    private const double DifferenceTolerance = 0.000000001;

    public StaminaChangeResult(
        double previous,
        double current,
        double requested,
        double applied)
    {
        if (!double.IsFinite(previous) || previous < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(previous),
                "Previous stamina must be finite and non-negative.");
        }

        if (!double.IsFinite(current) || current < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(current),
                "Current stamina must be finite and non-negative.");
        }

        if (!double.IsFinite(requested) || requested <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                "Requested stamina change must be finite and positive.");
        }

        if (!double.IsFinite(applied) || applied < 0.0 || applied > requested)
        {
            throw new ArgumentOutOfRangeException(
                nameof(applied),
                "Applied stamina must be finite and within the requested amount.");
        }

        double actualDifference = Math.Abs(current - previous);
        double allowedDifference = DifferenceTolerance * Math.Max(
            1.0,
            Math.Max(actualDifference, applied));
        if (Math.Abs(actualDifference - applied) > allowedDifference)
        {
            throw new ArgumentException(
                "Applied stamina must equal the absolute value change.",
                nameof(applied));
        }

        Previous = previous;
        Current = current;
        Requested = requested;
        Applied = applied;
    }

    public double Previous { get; }

    public double Current { get; }

    public double Requested { get; }

    public double Applied { get; }

    public bool Changed => Applied > 0.0;
}
