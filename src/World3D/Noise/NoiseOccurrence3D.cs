using System;
using Godot;
using LineZero.Gameplay.Noise;

namespace LineZero.World3D.Noise;

public sealed class NoiseOccurrence3D
{
    public NoiseOccurrence3D(NoiseEvent noise, Vector3 worldPosition)
    {
        ArgumentNullException.ThrowIfNull(noise);
        if (!IsFinite(worldPosition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldPosition),
                "3D noise position must be finite.");
        }

        Noise = noise;
        WorldPosition = worldPosition;
    }

    public NoiseEvent Noise { get; }

    public Vector3 WorldPosition { get; }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }
}
