using System;

namespace LineZero.Gameplay.Flashlight;

public readonly struct FlashlightChargeResult
{
    private const double DifferenceTolerance = 0.000000001;

    public FlashlightChargeResult(
        double requested,
        double applied,
        double previousCharge,
        double currentCharge,
        bool previousIsOn,
        bool currentIsOn,
        bool lowChargeReached,
        bool criticalChargeReached,
        bool depleted)
    {
        ValidateFiniteNonNegative(previousCharge, nameof(previousCharge));
        ValidateFiniteNonNegative(currentCharge, nameof(currentCharge));
        if (!double.IsFinite(requested) || requested <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                "Requested charge change must be finite and positive.");
        }

        if (!double.IsFinite(applied) || applied < 0.0 || applied > requested)
        {
            throw new ArgumentOutOfRangeException(
                nameof(applied),
                "Applied charge must be finite and within the requested amount.");
        }

        double actualDifference = Math.Abs(currentCharge - previousCharge);
        double tolerance = DifferenceTolerance * Math.Max(
            1.0,
            Math.Max(actualDifference, applied));
        if (Math.Abs(actualDifference - applied) > tolerance)
        {
            throw new ArgumentException(
                "Applied charge must equal the absolute charge change.",
                nameof(applied));
        }

        Requested = requested;
        Applied = applied;
        PreviousCharge = previousCharge;
        CurrentCharge = currentCharge;
        PreviousIsOn = previousIsOn;
        CurrentIsOn = currentIsOn;
        LowChargeReached = lowChargeReached;
        CriticalChargeReached = criticalChargeReached;
        Depleted = depleted;
    }

    public double Requested { get; }

    public double Applied { get; }

    public double PreviousCharge { get; }

    public double CurrentCharge { get; }

    public bool PreviousIsOn { get; }

    public bool CurrentIsOn { get; }

    public bool LowChargeReached { get; }

    public bool CriticalChargeReached { get; }

    public bool Depleted { get; }

    public bool ChargeChanged => Applied > 0.0;

    public bool OnStateChanged => PreviousIsOn != CurrentIsOn;

    public bool Changed => ChargeChanged || OnStateChanged;

    private static void ValidateFiniteNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Charge values must be finite and non-negative.");
        }
    }
}
