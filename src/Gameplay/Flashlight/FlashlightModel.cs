using System;
using LineZero.Core.Events;

namespace LineZero.Gameplay.Flashlight;

internal readonly record struct FlashlightChargeRestorationPlan(
    FlashlightModel Model,
    double RequestedAmount,
    double PreviousCharge,
    double CurrentCharge,
    double AppliedAmount,
    bool PreviousIsOn);

public sealed class FlashlightModel
{
    private const double MaximumFullChargeEpsilon = 0.05;

    public FlashlightModel(FlashlightDefinition definition, bool startOn = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();

        Definition = definition;
        MaximumCharge = definition.MaximumCharge;
        DrainPerSecond = definition.DrainPerSecond;
        LowChargeThreshold = definition.LowChargeThreshold;
        CriticalChargeThreshold = definition.CriticalChargeThreshold;
        ChargeRestoredPerBattery = definition.ChargeRestoredPerBattery;
        CurrentCharge = MaximumCharge;
        IsOn = startOn;
    }

    public FlashlightDefinition Definition { get; }

    public double MaximumCharge { get; }

    public double DrainPerSecond { get; }

    public double LowChargeThreshold { get; }

    public double CriticalChargeThreshold { get; }

    public double ChargeRestoredPerBattery { get; }

    public double CurrentCharge { get; private set; }

    public double NormalizedCharge => CurrentCharge / MaximumCharge;

    public bool IsOn { get; private set; }

    public bool IsDepleted => CurrentCharge == 0.0;

    public double FullChargeEpsilon =>
        Math.Min(MaximumFullChargeEpsilon, MaximumCharge * 0.0005);

    public bool IsEffectivelyFull =>
        MaximumCharge - CurrentCharge <= FullChargeEpsilon;

    public bool IsLow => !IsDepleted && CurrentCharge <= LowChargeThreshold;

    public bool IsCritical =>
        !IsDepleted && CurrentCharge <= CriticalChargeThreshold;

    public event Action? Changed;

    public event Action<bool>? PowerStateChanged;

    public event Action<FlashlightChargeResult>? Depleted;

    public event Action<FlashlightChargeResult>? LowChargeReached;

    public event Action<FlashlightChargeResult>? CriticalChargeReached;

    public FlashlightStateChangeResult TryTurnOn()
    {
        bool previousIsOn = IsOn;
        if (!IsOn && !IsDepleted)
        {
            IsOn = true;
        }

        FlashlightStateChangeResult result = new(
            requestedIsOn: true,
            previousIsOn,
            IsOn,
            CurrentCharge,
            CurrentCharge);
        if (result.Changed)
        {
            PublishChangedEvent();
            SafeEventPublisher.Publish(
                PowerStateChanged,
                IsOn,
                $"{nameof(FlashlightModel)}.{nameof(PowerStateChanged)}");
        }

        return result;
    }

    public FlashlightStateChangeResult TurnOff()
    {
        bool previousIsOn = IsOn;
        if (IsOn)
        {
            IsOn = false;
        }

        FlashlightStateChangeResult result = new(
            requestedIsOn: false,
            previousIsOn,
            IsOn,
            CurrentCharge,
            CurrentCharge);
        if (result.Changed)
        {
            PublishChangedEvent();
            SafeEventPublisher.Publish(
                PowerStateChanged,
                IsOn,
                $"{nameof(FlashlightModel)}.{nameof(PowerStateChanged)}");
        }

        return result;
    }

    public FlashlightStateChangeResult Toggle()
    {
        return IsOn ? TurnOff() : TryTurnOn();
    }

    public FlashlightChargeResult Drain(double amount)
    {
        ValidateChangeAmount(amount, nameof(amount));

        double previousCharge = CurrentCharge;
        bool previousIsOn = IsOn;
        if (!IsOn)
        {
            return CreateChargeResult(
                amount,
                applied: 0.0,
                previousCharge,
                previousCharge,
                previousIsOn,
                previousIsOn);
        }

        double applied = Math.Min(amount, previousCharge);
        double currentCharge = applied == previousCharge
            ? 0.0
            : previousCharge - applied;
        bool depleted = previousCharge > 0.0 && currentCharge == 0.0;
        bool currentIsOn = depleted ? false : previousIsOn;
        bool lowChargeReached =
            previousCharge > LowChargeThreshold &&
            currentCharge <= LowChargeThreshold;
        bool criticalChargeReached =
            previousCharge > CriticalChargeThreshold &&
            currentCharge <= CriticalChargeThreshold;

        FlashlightChargeResult result = new(
            amount,
            applied,
            previousCharge,
            currentCharge,
            previousIsOn,
            currentIsOn,
            lowChargeReached,
            criticalChargeReached,
            depleted);
        if (!result.Changed)
        {
            return result;
        }

        CurrentCharge = currentCharge;
        IsOn = currentIsOn;
        PublishChangedEvent();
        if (previousIsOn != currentIsOn)
        {
            SafeEventPublisher.Publish(
                PowerStateChanged,
                currentIsOn,
                $"{nameof(FlashlightModel)}.{nameof(PowerStateChanged)}");
        }

        if (lowChargeReached)
        {
            SafeEventPublisher.Publish(
                LowChargeReached,
                result,
                $"{nameof(FlashlightModel)}.{nameof(LowChargeReached)}");
        }

        if (criticalChargeReached)
        {
            SafeEventPublisher.Publish(
                CriticalChargeReached,
                result,
                $"{nameof(FlashlightModel)}.{nameof(CriticalChargeReached)}");
        }

        if (depleted)
        {
            SafeEventPublisher.Publish(
                Depleted,
                result,
                $"{nameof(FlashlightModel)}.{nameof(Depleted)}");
        }

        return result;
    }

    public FlashlightChargeResult RestoreCharge(double amount)
    {
        FlashlightChargeResult result = RestoreChargeWithoutNotification(amount);
        PublishChanged(result);
        return result;
    }

    public double CalculateRestorableCharge(double requestedAmount)
    {
        ValidateChangeAmount(requestedAmount, nameof(requestedAmount));
        double availableCapacity = MaximumCharge - CurrentCharge;
        if (!double.IsFinite(availableCapacity))
        {
            throw new InvalidOperationException(
                "Flashlight available capacity must be finite.");
        }

        if (availableCapacity <= FullChargeEpsilon)
        {
            return 0.0;
        }

        return Math.Min(requestedAmount, availableCapacity);
    }

    internal bool TryPrepareChargeRestoration(
        double requestedAmount,
        out FlashlightChargeRestorationPlan plan)
    {
        ValidateChangeAmount(requestedAmount, nameof(requestedAmount));

        double previousCharge = CurrentCharge;
        double applied = CalculateRestorableCharge(requestedAmount);
        if (applied <= FullChargeEpsilon)
        {
            plan = default;
            return false;
        }

        double currentCharge = Math.Min(MaximumCharge, previousCharge + applied);
        if (MaximumCharge - currentCharge <= FullChargeEpsilon)
        {
            currentCharge = MaximumCharge;
            applied = currentCharge - previousCharge;
        }

        if (applied <= FullChargeEpsilon)
        {
            plan = default;
            return false;
        }

        plan = new FlashlightChargeRestorationPlan(
            this,
            requestedAmount,
            previousCharge,
            currentCharge,
            applied,
            IsOn);
        return true;
    }

    internal bool CanApply(FlashlightChargeRestorationPlan plan)
    {
        return ReferenceEquals(plan.Model, this) &&
               double.IsFinite(plan.RequestedAmount) &&
               plan.RequestedAmount > 0.0 &&
               double.IsFinite(plan.PreviousCharge) &&
               double.IsFinite(plan.CurrentCharge) &&
               double.IsFinite(plan.AppliedAmount) &&
               plan.AppliedAmount > FullChargeEpsilon &&
               plan.PreviousCharge == CurrentCharge &&
               plan.PreviousIsOn == IsOn &&
               plan.CurrentCharge > plan.PreviousCharge &&
               plan.CurrentCharge <= MaximumCharge &&
               Math.Abs(
                   plan.CurrentCharge - plan.PreviousCharge - plan.AppliedAmount) <=
                   0.000000001;
    }

    internal FlashlightChargeResult ApplyWithoutNotification(
        FlashlightChargeRestorationPlan plan)
    {
        if (!CanApply(plan))
        {
            throw new InvalidOperationException(
                "The prepared flashlight charge restoration is no longer valid.");
        }

        FlashlightChargeResult result = CreateChargeResult(
            plan.RequestedAmount,
            plan.AppliedAmount,
            plan.PreviousCharge,
            plan.CurrentCharge,
            plan.PreviousIsOn,
            plan.PreviousIsOn);
        CurrentCharge = plan.CurrentCharge;
        return result;
    }

    internal FlashlightChargeResult RestoreChargeWithoutNotification(double amount)
    {
        if (!TryPrepareChargeRestoration(amount, out FlashlightChargeRestorationPlan plan))
        {
            bool previousIsOn = IsOn;
            return CreateChargeResult(
                amount,
                applied: 0.0,
                CurrentCharge,
                CurrentCharge,
                previousIsOn,
                previousIsOn);
        }

        return ApplyWithoutNotification(plan);
    }

    internal void PublishChanged(FlashlightChargeResult result)
    {
        if (result.Changed)
        {
            PublishChangedEvent();
        }
    }

    private void PublishChangedEvent()
    {
        SafeEventPublisher.Publish(
            Changed,
            $"{nameof(FlashlightModel)}.{nameof(Changed)}");
    }

    private static FlashlightChargeResult CreateChargeResult(
        double requested,
        double applied,
        double previousCharge,
        double currentCharge,
        bool previousIsOn,
        bool currentIsOn)
    {
        return new FlashlightChargeResult(
            requested,
            applied,
            previousCharge,
            currentCharge,
            previousIsOn,
            currentIsOn,
            lowChargeReached: false,
            criticalChargeReached: false,
            depleted: false);
    }

    private static void ValidateChangeAmount(double amount, string parameterName)
    {
        if (!double.IsFinite(amount) || amount <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Flashlight charge changes must be finite and positive.");
        }
    }
}
