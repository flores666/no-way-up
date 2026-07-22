using System;

namespace LineZero.Gameplay.Interaction;

public static class InteractionCandidateScorer
{
    private const float DistanceWeight = 0.55f;
    private const float AlignmentWeight = 0.40f;
    private const float PriorityWeight = 0.05f;
    private const float MaximumPriorityMagnitude = 10.0f;

    public static float Calculate(
        float distance,
        float interactionRange,
        float alignment,
        int priority)
    {
        if (!float.IsFinite(distance) || distance < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance),
                "Interaction distance must be finite and non-negative.");
        }

        if (!float.IsFinite(interactionRange) || interactionRange <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interactionRange),
                "Interaction range must be finite and positive.");
        }

        if (!float.IsFinite(alignment))
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignment),
                "Interaction alignment must be finite.");
        }

        float normalizedDistance = Math.Clamp(
            distance / interactionRange,
            0.0f,
            1.0f);
        float distanceScore = 1.0f - normalizedDistance;
        float alignmentScore =
            (Math.Clamp(alignment, -1.0f, 1.0f) + 1.0f) * 0.5f;
        float priorityScore = Math.Clamp(
            priority / MaximumPriorityMagnitude,
            -1.0f,
            1.0f);

        return (distanceScore * DistanceWeight) +
               (alignmentScore * AlignmentWeight) +
               (priorityScore * PriorityWeight);
    }

    public static bool IsClearlyBetter(
        float currentScore,
        float challengerScore,
        float switchThreshold)
    {
        if (!float.IsFinite(currentScore) ||
            !float.IsFinite(challengerScore) ||
            !float.IsFinite(switchThreshold) ||
            switchThreshold < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(switchThreshold),
                "Candidate scores must be finite and the threshold non-negative.");
        }

        return challengerScore > currentScore + switchThreshold;
    }
}
