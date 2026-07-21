using System;
using Godot;
using LineZero.Gameplay.Noise;

namespace LineZero.World2D.Noise;

public sealed class NoiseOccurrence2D
{
    public NoiseOccurrence2D(NoiseEvent noise, Vector2 worldPosition)
    {
        ArgumentNullException.ThrowIfNull(noise);
        if (!float.IsFinite(worldPosition.X) || !float.IsFinite(worldPosition.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldPosition),
                "Noise positions must be finite.");
        }

        Noise = noise;
        WorldPosition = worldPosition;
    }

    public NoiseEvent Noise { get; }

    public Vector2 WorldPosition { get; }
}
