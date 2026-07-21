using System;

namespace LineZero.World2D.Noise;

public sealed class PerceivedNoise2D
{
    public PerceivedNoise2D(
        NoiseOccurrence2D occurrence,
        float perceivedIntensity,
        float distance,
        float effectiveHearingRadius,
        bool wasOccluded)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (!float.IsFinite(perceivedIntensity) || perceivedIntensity <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(perceivedIntensity));
        }

        if (!float.IsFinite(distance) || distance < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(distance));
        }

        if (!float.IsFinite(effectiveHearingRadius) || effectiveHearingRadius <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveHearingRadius));
        }

        Occurrence = occurrence;
        PerceivedIntensity = perceivedIntensity;
        Distance = distance;
        EffectiveHearingRadius = effectiveHearingRadius;
        WasOccluded = wasOccluded;
    }

    public NoiseOccurrence2D Occurrence { get; }

    public float PerceivedIntensity { get; }

    public float Distance { get; }

    public float EffectiveHearingRadius { get; }

    public bool WasOccluded { get; }
}
