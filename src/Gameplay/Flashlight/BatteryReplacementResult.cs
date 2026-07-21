using System;

namespace LineZero.Gameplay.Flashlight;

public readonly struct BatteryReplacementResult
{
    private const double DifferenceTolerance = 0.000000001;

    public BatteryReplacementResult(
        bool success,
        bool batteryConsumed,
        double previousCharge,
        double currentCharge,
        double restoredCharge,
        string message)
    {
        if (!double.IsFinite(previousCharge) || previousCharge < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(previousCharge),
                "Previous charge must be finite and non-negative.");
        }

        if (!double.IsFinite(currentCharge) || currentCharge < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentCharge),
                "Current charge must be finite and non-negative.");
        }

        if (!double.IsFinite(restoredCharge) || restoredCharge < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(restoredCharge),
                "Restored charge must be finite and non-negative.");
        }

        double actualRestoredCharge = currentCharge - previousCharge;
        double tolerance = DifferenceTolerance * Math.Max(
            1.0,
            Math.Max(Math.Abs(actualRestoredCharge), restoredCharge));
        if (actualRestoredCharge < 0.0 ||
            Math.Abs(actualRestoredCharge - restoredCharge) > tolerance)
        {
            throw new ArgumentException(
                "Restored charge must equal the increase in current charge.",
                nameof(restoredCharge));
        }

        if (success != batteryConsumed || success != (restoredCharge > 0.0))
        {
            throw new ArgumentException(
                "Successful replacement must consume one battery and restore charge.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "Battery replacement result requires a message.",
                nameof(message));
        }

        Success = success;
        BatteryConsumed = batteryConsumed;
        PreviousCharge = previousCharge;
        CurrentCharge = currentCharge;
        RestoredCharge = restoredCharge;
        Message = message;
    }

    public bool Success { get; }

    public bool BatteryConsumed { get; }

    public double PreviousCharge { get; }

    public double CurrentCharge { get; }

    public double RestoredCharge { get; }

    public string Message { get; }
}
