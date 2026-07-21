using System;

namespace LineZero.Gameplay.Items;

public sealed class ItemUseResult
{
    public ItemUseResult(
        bool success,
        bool itemConsumed,
        string message,
        int? appliedAmount = null)
    {
        if (itemConsumed && !success)
        {
            throw new ArgumentException(
                "A failed item use cannot report a consumed item.",
                nameof(itemConsumed));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "Item-use result message must be non-empty.",
                nameof(message));
        }

        if (appliedAmount is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appliedAmount),
                "Applied amount must be positive when supplied.");
        }

        Success = success;
        ItemConsumed = itemConsumed;
        Message = message.Trim();
        AppliedAmount = appliedAmount;
    }

    public bool Success { get; }

    public bool ItemConsumed { get; }

    public string Message { get; }

    public int? AppliedAmount { get; }

    public static ItemUseResult Failure(string message)
    {
        return new ItemUseResult(false, false, message);
    }

    public static ItemUseResult Allowed(string message)
    {
        return new ItemUseResult(true, false, message);
    }

    public static ItemUseResult EffectApplied(string message, int? appliedAmount = null)
    {
        return new ItemUseResult(true, false, message, appliedAmount);
    }

    public static ItemUseResult Consumed(string message, int? appliedAmount = null)
    {
        return new ItemUseResult(true, true, message, appliedAmount);
    }
}
