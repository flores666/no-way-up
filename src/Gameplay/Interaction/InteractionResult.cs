using System;

namespace LineZero.Gameplay.Interaction;

public readonly record struct InteractionResult
{
    private InteractionResult(string message)
    {
        Message = message;
    }

    public string? Message { get; }

    public static InteractionResult None => default;

    public static InteractionResult WithMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("An interaction message cannot be empty.", nameof(message));
        }

        return new InteractionResult(message);
    }
}

