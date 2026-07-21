using System;

namespace LineZero.Gameplay.Flashlight;

public readonly struct FlashlightStateChangeResult
{
    public FlashlightStateChangeResult(
        bool requestedIsOn,
        bool previousIsOn,
        bool currentIsOn,
        double previousCharge,
        double currentCharge)
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

        RequestedIsOn = requestedIsOn;
        PreviousIsOn = previousIsOn;
        CurrentIsOn = currentIsOn;
        PreviousCharge = previousCharge;
        CurrentCharge = currentCharge;
    }

    public bool RequestedIsOn { get; }

    public bool PreviousIsOn { get; }

    public bool CurrentIsOn { get; }

    public double PreviousCharge { get; }

    public double CurrentCharge { get; }

    public bool Applied => CurrentIsOn == RequestedIsOn;

    public bool Changed => PreviousIsOn != CurrentIsOn;
}
