using System;

namespace LineZero.Gameplay.Noise;

/// <summary>
/// Accumulates actual travelled distance as normalized footstep progress.
/// Emission is deliberately separate: adapters may drain only a bounded number
/// of pending steps per update, while all remaining step debt stays here.
/// </summary>
public sealed class FootstepCadenceModel
{
    private const double CompletionTolerance = 0.000000001;
    private const double MaximumNormalizedProgressPerAdvance = 1_000_000.0;

    private double _cycleProgress;
    private double _cycleWeightedIntensity;
    private long _pendingSteps;
    private double _pendingIntensitySum;

    public double CycleProgress => _cycleProgress;

    public long PendingSteps => _pendingSteps;

    public FootstepCadenceAdvanceResult Advance(
        float travelledDistance,
        float stepDistance,
        float intensity)
    {
        ValidatePositiveFinite(travelledDistance, nameof(travelledDistance));
        ValidatePositiveFinite(stepDistance, nameof(stepDistance));
        ValidatePositiveFinite(intensity, nameof(intensity));

        double progress = travelledDistance / stepDistance;
        if (!double.IsFinite(progress) ||
            progress <= 0.0 ||
            progress > MaximumNormalizedProgressPerAdvance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelledDistance),
                "Footstep progress is outside the supported finite range.");
        }

        long completedSteps = 0;
        double progressNeeded = 1.0 - _cycleProgress;
        if (progress + CompletionTolerance >= progressNeeded)
        {
            double completedIntensity =
                _cycleWeightedIntensity + (progressNeeded * intensity);
            AddPendingSteps(1, completedIntensity);
            completedSteps++;
            progress -= progressNeeded;
            _cycleProgress = 0.0;
            _cycleWeightedIntensity = 0.0;
        }
        else
        {
            _cycleProgress += progress;
            _cycleWeightedIntensity += progress * intensity;
            return new FootstepCadenceAdvanceResult(
                completedSteps,
                _pendingSteps,
                _cycleProgress);
        }

        double wholeStepsAsDouble = Math.Floor(progress + CompletionTolerance);
        if (wholeStepsAsDouble > 0.0)
        {
            if (wholeStepsAsDouble > long.MaxValue)
            {
                throw new InvalidOperationException(
                    "Footstep debt exceeded its supported range.");
            }

            long wholeSteps = (long)wholeStepsAsDouble;
            AddPendingSteps(wholeSteps, wholeSteps * (double)intensity);
            completedSteps = checked(completedSteps + wholeSteps);
            progress -= wholeSteps;
        }

        _cycleProgress = Math.Clamp(progress, 0.0, 1.0 - CompletionTolerance);
        _cycleWeightedIntensity = _cycleProgress * intensity;
        return new FootstepCadenceAdvanceResult(
            completedSteps,
            _pendingSteps,
            _cycleProgress);
    }

    public bool TryTakePendingStep(out float intensity)
    {
        if (_pendingSteps == 0)
        {
            intensity = 0.0f;
            return false;
        }

        double averageIntensity = _pendingIntensitySum / _pendingSteps;
        if (!double.IsFinite(averageIntensity) || averageIntensity <= 0.0)
        {
            throw new InvalidOperationException(
                "Pending footstep intensity debt is invalid.");
        }

        intensity = (float)averageIntensity;
        _pendingSteps--;
        if (_pendingSteps == 0)
        {
            _pendingIntensitySum = 0.0;
        }
        else
        {
            _pendingIntensitySum -= averageIntensity;
        }

        return true;
    }

    public void Reset()
    {
        _cycleProgress = 0.0;
        _cycleWeightedIntensity = 0.0;
        _pendingSteps = 0;
        _pendingIntensitySum = 0.0;
    }

    private void AddPendingSteps(long count, double intensitySum)
    {
        if (count < 1 || !double.IsFinite(intensitySum) || intensitySum <= 0.0)
        {
            throw new InvalidOperationException(
                "Completed footstep debt must be finite and positive.");
        }

        _pendingSteps = checked(_pendingSteps + count);
        _pendingIntensitySum += intensitySum;
        if (!double.IsFinite(_pendingIntensitySum))
        {
            throw new InvalidOperationException(
                "Footstep intensity debt exceeded its supported range.");
        }
    }

    private static void ValidatePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Footstep values must be finite and positive.");
        }
    }
}
