using System;
using LineZero.Core.Events;

namespace LineZero.Gameplay.Movement;

public sealed class StaminaModel
{
    public StaminaModel(double maximum)
    {
        if (!double.IsFinite(maximum) || maximum <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                "Maximum stamina must be finite and positive.");
        }

        Maximum = maximum;
        Current = maximum;
    }

    public double Maximum { get; }

    public double Current { get; private set; }

    public double Normalized => Current / Maximum;

    public bool IsEmpty => Current == 0.0;

    public bool IsFull => Current == Maximum;

    public event Action<StaminaChangeResult>? Changed;

    public event Action<StaminaChangeResult>? Depleted;

    public event Action<StaminaChangeResult>? RecoveredFromEmpty;

    public StaminaChangeResult Consume(double amount)
    {
        ValidateChangeAmount(amount, nameof(amount));

        double previous = Current;
        double applied = Math.Min(amount, previous);
        double current = applied == previous ? 0.0 : previous - applied;
        StaminaChangeResult result = new(previous, current, amount, applied);
        if (!result.Changed)
        {
            return result;
        }

        Current = current;
        SafeEventPublisher.Publish(
            Changed,
            result,
            $"{nameof(StaminaModel)}.{nameof(Changed)}");
        if (previous > 0.0 && current == 0.0)
        {
            SafeEventPublisher.Publish(
                Depleted,
                result,
                $"{nameof(StaminaModel)}.{nameof(Depleted)}");
        }

        return result;
    }

    public StaminaChangeResult Restore(double amount)
    {
        ValidateChangeAmount(amount, nameof(amount));

        double previous = Current;
        double availableCapacity = Maximum - previous;
        double applied = Math.Min(amount, availableCapacity);
        double current = applied == availableCapacity
            ? Maximum
            : previous + applied;
        StaminaChangeResult result = new(previous, current, amount, applied);
        if (!result.Changed)
        {
            return result;
        }

        Current = current;
        SafeEventPublisher.Publish(
            Changed,
            result,
            $"{nameof(StaminaModel)}.{nameof(Changed)}");
        if (previous == 0.0 && current > 0.0)
        {
            SafeEventPublisher.Publish(
                RecoveredFromEmpty,
                result,
                $"{nameof(StaminaModel)}.{nameof(RecoveredFromEmpty)}");
        }

        return result;
    }

    private static void ValidateChangeAmount(double amount, string parameterName)
    {
        if (!double.IsFinite(amount) || amount <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Stamina changes must be finite and positive.");
        }
    }
}
