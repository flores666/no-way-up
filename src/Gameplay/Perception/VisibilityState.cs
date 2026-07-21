using System;

namespace LineZero.Gameplay.Perception;

public readonly struct VisibilityState
{
    public VisibilityState(
        float postureMultiplier,
        float ambientLightMultiplier,
        float flashlightMultiplier,
        float finalMultiplier,
        VisibilityCategory category,
        bool isActorAlive,
        string ambientZoneName)
    {
        ValidateMultiplier(postureMultiplier, nameof(postureMultiplier));
        ValidateMultiplier(ambientLightMultiplier, nameof(ambientLightMultiplier));
        ValidateMultiplier(flashlightMultiplier, nameof(flashlightMultiplier));
        ValidateMultiplier(finalMultiplier, nameof(finalMultiplier));
        if (string.IsNullOrWhiteSpace(ambientZoneName))
        {
            throw new ArgumentException(
                "Ambient zone name must be non-empty.",
                nameof(ambientZoneName));
        }

        PostureMultiplier = postureMultiplier;
        AmbientLightMultiplier = ambientLightMultiplier;
        FlashlightMultiplier = flashlightMultiplier;
        FinalMultiplier = finalMultiplier;
        Category = category;
        IsActorAlive = isActorAlive;
        AmbientZoneName = ambientZoneName;
    }

    public float PostureMultiplier { get; }

    public float AmbientLightMultiplier { get; }

    public float FlashlightMultiplier { get; }

    public float FinalMultiplier { get; }

    public VisibilityCategory Category { get; }

    public bool IsActorAlive { get; }

    public string AmbientZoneName { get; }

    private static void ValidateMultiplier(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Visibility multipliers must be finite and positive.");
        }
    }
}
