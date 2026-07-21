using System;
using Godot;

namespace LineZero.Data;

[GlobalClass]
public sealed partial class PlayerMovementSettings : Resource
{
    private const float ExistingCrouchVisibilityMultiplier = 0.65f;
    private const float ExistingCrouchFootstepIntensity = 0.45f;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0,or_greater")]
    public float WalkSpeed { get; set; } = 220.0f;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0,or_greater")]
    public float CrouchSpeed { get; set; } = 121.0f;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0,or_greater")]
    public float CrawlSpeed { get; set; } = 77.0f;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0,or_greater")]
    public float SprintSpeed { get; set; } = 341.0f;

    [Export(PropertyHint.Range, "1.0,5000.0,10.0,or_greater")]
    public float Acceleration { get; set; } = 1250.0f;

    [Export(PropertyHint.Range, "1.0,5000.0,10.0,or_greater")]
    public float Deceleration { get; set; } = 1550.0f;

    [Export(PropertyHint.Range, "0.01,1000.0,0.01,or_greater")]
    public double SprintStaminaCostPerSecond { get; set; } = 25.0;

    [Export(PropertyHint.Range, "0.01,1000.0,0.01,or_greater")]
    public double StaminaRecoveryPerSecond { get; set; } = 18.0;

    [Export(PropertyHint.Range, "0.0,30.0,0.05,or_greater")]
    public double StaminaRecoveryDelaySeconds { get; set; } = 0.75;

    [Export(PropertyHint.Range, "0.0,1000.0,0.1,or_greater")]
    public double MinimumStaminaToStartSprint { get; set; } = 10.0;

    [Export(PropertyHint.Range, "0.01,1000.0,0.1,or_greater")]
    public double MaximumStamina { get; set; } = 100.0;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float CrawlVisibilityMultiplier { get; set; } = 0.40f;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float CrawlFootstepIntensityMultiplier { get; set; } = 0.20f;

    [Export(PropertyHint.Range, "1.0,5.0,0.05,or_greater")]
    public float CrawlStepDistanceMultiplier { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.001,10.0,0.001,or_greater")]
    public float MinimumActualMovementDistance { get; set; } = 0.05f;

    public void Validate()
    {
        if (!float.IsFinite(CrawlSpeed) || CrawlSpeed <= 0.0f ||
            !float.IsFinite(CrouchSpeed) || CrouchSpeed <= 0.0f ||
            !float.IsFinite(WalkSpeed) || WalkSpeed <= 0.0f ||
            !float.IsFinite(SprintSpeed) || SprintSpeed <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires positive finite speeds.");
        }

        if (!(CrawlSpeed < CrouchSpeed &&
              CrouchSpeed < WalkSpeed &&
              WalkSpeed < SprintSpeed))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires " +
                "CrawlSpeed < CrouchSpeed < WalkSpeed < SprintSpeed.");
        }

        if (!float.IsFinite(Acceleration) || Acceleration <= 0.0f ||
            !float.IsFinite(Deceleration) || Deceleration <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires positive finite acceleration and deceleration.");
        }

        if (!double.IsFinite(MaximumStamina) || MaximumStamina <= 0.0 ||
            !double.IsFinite(SprintStaminaCostPerSecond) || SprintStaminaCostPerSecond <= 0.0 ||
            !double.IsFinite(StaminaRecoveryPerSecond) || StaminaRecoveryPerSecond <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires positive finite stamina tuning.");
        }

        if (!double.IsFinite(StaminaRecoveryDelaySeconds) ||
            StaminaRecoveryDelaySeconds < 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires a non-negative finite stamina recovery delay.");
        }

        if (!double.IsFinite(MinimumStaminaToStartSprint) ||
            MinimumStaminaToStartSprint < 0.0 ||
            MinimumStaminaToStartSprint > MaximumStamina)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires minimum sprint stamina within 0..MaximumStamina.");
        }

        if (!float.IsFinite(CrawlVisibilityMultiplier) ||
            CrawlVisibilityMultiplier <= 0.0f ||
            CrawlVisibilityMultiplier >= ExistingCrouchVisibilityMultiplier)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires a positive " +
                "crawl visibility multiplier below the crouch multiplier.");
        }

        if (!float.IsFinite(CrawlFootstepIntensityMultiplier) ||
            CrawlFootstepIntensityMultiplier <= 0.0f ||
            CrawlFootstepIntensityMultiplier >= ExistingCrouchFootstepIntensity)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires a positive " +
                "crawl footstep intensity multiplier below crouch intensity.");
        }

        if (!float.IsFinite(CrawlStepDistanceMultiplier) ||
            CrawlStepDistanceMultiplier <= 1.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires a finite " +
                "crawl step distance multiplier greater than one.");
        }

        if (!float.IsFinite(MinimumActualMovementDistance) ||
            MinimumActualMovementDistance <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMovementSettings)} at '{GetDisplayPath()}' requires a positive " +
                "finite minimum actual movement distance.");
        }
    }

    private string GetDisplayPath()
    {
        return string.IsNullOrWhiteSpace(ResourcePath)
            ? "<unsaved resource>"
            : ResourcePath;
    }
}
