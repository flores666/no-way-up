using System;
using LineZero.Gameplay.Movement;

namespace LineZero.Gameplay.Perception;

public static class VisibilityRules
{
    public const float DefaultAmbientLightMultiplier = 1.0f;
    public const string DefaultAmbientZoneName = "Normal area";

    private const float WalkVisibilityMultiplier = 1.0f;
    private const float CrouchVisibilityMultiplier = 0.65f;
    private const float SprintVisibilityMultiplier = 1.15f;
    private const float HiddenThreshold = 0.55f;
    private const float DimThreshold = 0.85f;
    private const float ExposedThreshold = 1.30f;

    public static VisibilityState Calculate(
        MovementMode movementMode,
        float crawlVisibilityMultiplier,
        float ambientLightMultiplier,
        bool flashlightIsOn,
        float flashlightOnMultiplier,
        bool actorIsAlive,
        string ambientZoneName)
    {
        ValidatePositiveFinite(
            crawlVisibilityMultiplier,
            nameof(crawlVisibilityMultiplier));
        ValidatePositiveFinite(
            ambientLightMultiplier,
            nameof(ambientLightMultiplier));
        if (!float.IsFinite(flashlightOnMultiplier) ||
            flashlightOnMultiplier <= 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flashlightOnMultiplier),
                "Flashlight visibility multiplier must be finite and greater than one.");
        }

        if (string.IsNullOrWhiteSpace(ambientZoneName))
        {
            throw new ArgumentException(
                "Ambient zone name must be non-empty.",
                nameof(ambientZoneName));
        }

        float postureMultiplier = movementMode switch
        {
            MovementMode.Walk => WalkVisibilityMultiplier,
            MovementMode.Crouch => CrouchVisibilityMultiplier,
            MovementMode.Sprint => SprintVisibilityMultiplier,
            MovementMode.Crawl => crawlVisibilityMultiplier,
            _ => throw new ArgumentOutOfRangeException(nameof(movementMode))
        };
        float flashlightMultiplier = flashlightIsOn
            ? flashlightOnMultiplier
            : 1.0f;
        float finalMultiplier =
            postureMultiplier * ambientLightMultiplier * flashlightMultiplier;
        if (!float.IsFinite(finalMultiplier) || finalMultiplier <= 0.0f)
        {
            throw new InvalidOperationException(
                "Calculated visibility must be finite and positive.");
        }

        VisibilityCategory category = finalMultiplier switch
        {
            < HiddenThreshold => VisibilityCategory.Hidden,
            < DimThreshold => VisibilityCategory.Dim,
            < ExposedThreshold => VisibilityCategory.Visible,
            _ => VisibilityCategory.Exposed
        };
        return new VisibilityState(
            postureMultiplier,
            ambientLightMultiplier,
            flashlightMultiplier,
            finalMultiplier,
            category,
            actorIsAlive,
            ambientZoneName);
    }

    private static void ValidatePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Visibility multiplier must be finite and positive.");
        }
    }
}
