using System;
using Godot;

namespace LineZero.Gameplay.Enemies;

[GlobalClass]
public sealed partial class MutantDefinition : Resource
{
    private string _id = string.Empty;
    private string _displayName = string.Empty;

    [Export]
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    [Export]
    public string DisplayName
    {
        get => _displayName;
        set => _displayName = value ?? string.Empty;
    }

    [Export(PropertyHint.Range, "1,9999,1,or_greater")]
    public int MaxHealth { get; set; } = 75;

    [Export(PropertyHint.Range, "1.0,1000.0,1.0,or_greater")]
    public float MoveSpeed { get; set; } = 90.0f;

    [Export(PropertyHint.Range, "1.0,5000.0,1.0,or_greater")]
    public float Acceleration { get; set; } = 500.0f;

    [Export(PropertyHint.Range, "1.0,5000.0,1.0,or_greater")]
    public float SightRange { get; set; } = 320.0f;

    [Export(PropertyHint.Range, "0.1,360.0,0.1")]
    public float FieldOfViewDegrees { get; set; } = 110.0f;

    [Export(PropertyHint.Range, "0.01,10.0,0.01,or_greater")]
    public double PerceptionIntervalSeconds { get; set; } = 0.12;

    [Export(PropertyHint.Range, "0.01,10.0,0.01,or_greater")]
    public double ChasePathRefreshIntervalSeconds { get; set; } = 0.15;

    [Export(PropertyHint.Range, "0.01,30.0,0.01,or_greater")]
    public double LostTargetGraceSeconds { get; set; } = 2.0;

    [Export(PropertyHint.Range, "0.01,10.0,0.01,or_greater")]
    public float HearingSensitivity { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "0.01,10.0,0.01,or_greater")]
    public float MinimumAudibleIntensity { get; set; } = 0.2f;

    [Export(PropertyHint.Range, "0.1,30.0,0.1,or_greater")]
    public double InvestigationDurationSeconds { get; set; } = 2.0;

    [Export(PropertyHint.Range, "0.1,60.0,0.1,or_greater")]
    public double MaximumSearchSeconds { get; set; } = 5.0;

    [Export(PropertyHint.Range, "0.1,30.0,0.1,or_greater")]
    public double StuckDetectionSeconds { get; set; } = 1.25;

    [Export(PropertyHint.Range, "0.1,100.0,0.1,or_greater")]
    public float MinimumProgressDistance { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "1.0,500.0,1.0,or_greater")]
    public float AttackRange { get; set; } = 34.0f;

    [Export(PropertyHint.Range, "1,9999,1,or_greater")]
    public int AttackDamage { get; set; } = 15;

    [Export(PropertyHint.Range, "0.01,60.0,0.01,or_greater")]
    public double AttackCooldownSeconds { get; set; } = 1.0;

    [Export(PropertyHint.Range, "0.01,60.0,0.01,or_greater")]
    public double PatrolWaitSeconds { get; set; } = 1.0;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} at '{GetDisplayPath()}' requires a non-empty ID.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires a non-empty display name.");
        }

        if (MaxHealth < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires positive maximum health.");
        }

        if (!float.IsFinite(MoveSpeed) || MoveSpeed <= 0.0f ||
            !float.IsFinite(Acceleration) || Acceleration <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires positive movement tuning.");
        }

        if (!float.IsFinite(SightRange) || SightRange <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires positive sight range.");
        }

        if (!float.IsFinite(FieldOfViewDegrees) ||
            FieldOfViewDegrees <= 0.0f ||
            FieldOfViewDegrees > 360.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires field of view within 0..360 degrees.");
        }

        if (!double.IsFinite(PerceptionIntervalSeconds) ||
            PerceptionIntervalSeconds <= 0.0 ||
            !double.IsFinite(ChasePathRefreshIntervalSeconds) ||
            ChasePathRefreshIntervalSeconds <= 0.0 ||
            !double.IsFinite(LostTargetGraceSeconds) ||
            LostTargetGraceSeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires positive perception and chase timing.");
        }

        if (!float.IsFinite(HearingSensitivity) || HearingSensitivity <= 0.0f ||
            !float.IsFinite(MinimumAudibleIntensity) || MinimumAudibleIntensity <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires positive finite hearing tuning.");
        }

        if (!double.IsFinite(InvestigationDurationSeconds) ||
            InvestigationDurationSeconds <= 0.0 ||
            !double.IsFinite(MaximumSearchSeconds) ||
            MaximumSearchSeconds <= 0.0 ||
            !double.IsFinite(StuckDetectionSeconds) ||
            StuckDetectionSeconds <= 0.0 ||
            !float.IsFinite(MinimumProgressDistance) ||
            MinimumProgressDistance <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires positive search and stuck-recovery tuning.");
        }

        if (!float.IsFinite(AttackRange) ||
            AttackRange <= 0.0f ||
            AttackRange > SightRange)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires positive attack range no greater than sight range.");
        }

        if (AttackDamage < 1 ||
            !double.IsFinite(AttackCooldownSeconds) ||
            AttackCooldownSeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires positive attack damage and cooldown.");
        }

        if (!double.IsFinite(PatrolWaitSeconds) || PatrolWaitSeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(MutantDefinition)} '{Id}' requires a positive patrol wait.");
        }
    }

    private string GetDisplayPath()
    {
        return string.IsNullOrWhiteSpace(ResourcePath) ? "<unsaved resource>" : ResourcePath;
    }
}
