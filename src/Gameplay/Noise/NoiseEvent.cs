using System;
using Godot;

namespace LineZero.Gameplay.Noise;

public sealed class NoiseEvent
{
    public NoiseEvent(
        Node source,
        NoiseKind kind,
        float intensity,
        ulong sequenceId,
        double timestampSeconds,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!GodotObject.IsInstanceValid(source) || !source.IsInsideTree())
        {
            throw new ArgumentException(
                "A noise source must be an active scene node.",
                nameof(source));
        }

        if (!Enum.IsDefined(typeof(NoiseKind), kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!float.IsFinite(intensity) || intensity <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intensity),
                "Noise intensity must be finite and positive.");
        }

        if (sequenceId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceId),
                "Noise sequence identifiers start at one.");
        }

        if (!double.IsFinite(timestampSeconds) || timestampSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestampSeconds),
                "Noise timestamps must be finite and non-negative.");
        }

        if (description is not null && string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "A supplied noise description cannot be empty.",
                nameof(description));
        }

        Source = source;
        Kind = kind;
        Intensity = intensity;
        SequenceId = sequenceId;
        TimestampSeconds = timestampSeconds;
        Description = description;
    }

    public Node Source { get; }

    public NoiseKind Kind { get; }

    public float Intensity { get; }

    public ulong SequenceId { get; }

    public double TimestampSeconds { get; }

    public string? Description { get; }
}
