using System;

namespace LineZero.World3D.Noise;

public sealed class PerceivedNoise3D
{
    public PerceivedNoise3D(
        NoiseOccurrence3D occurrence,
        float perceivedIntensity,
        float distance,
        float effectiveRadius,
        int barrierCount)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (!float.IsFinite(perceivedIntensity) || perceivedIntensity <= 0.0f ||
            !float.IsFinite(distance) || distance < 0.0f ||
            !float.IsFinite(effectiveRadius) || effectiveRadius <= 0.0f ||
            barrierCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(perceivedIntensity),
                "Perceived 3D noise values are invalid.");
        }

        Occurrence = occurrence;
        PerceivedIntensity = perceivedIntensity;
        Distance = distance;
        EffectiveRadius = effectiveRadius;
        BarrierCount = barrierCount;
    }

    public NoiseOccurrence3D Occurrence { get; }

    public float PerceivedIntensity { get; }

    public float Distance { get; }

    public float EffectiveRadius { get; }

    public int BarrierCount { get; }

    public bool WasOccluded => BarrierCount > 0;
}
