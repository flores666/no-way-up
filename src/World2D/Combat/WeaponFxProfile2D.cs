using System;
using Godot;

namespace LineZero.World2D.Combat;

[GlobalClass]
public sealed partial class WeaponFxProfile2D : Resource
{
    [Export]
    public Texture2D? MuzzleFlashTexture { get; set; }

    [Export]
    public Texture2D? BulletTexture { get; set; }

    [Export]
    public bool UseProceduralMuzzleFlash { get; set; } = true;

    [Export(PropertyHint.Range, "1,16,1")]
    public int MuzzleFlashFrameCount { get; set; } = 4;

    [Export(PropertyHint.Range, "0.01,0.3,0.005")]
    public float MuzzleFlashDurationSeconds { get; set; } = 0.08f;

    [Export(PropertyHint.Range, "0.05,4.0,0.025")]
    public float MuzzleFlashScale { get; set; } = 0.425f;

    [Export]
    public Vector2 MuzzleFlashLocalOffset { get; set; } = new(5.0f, 0.0f);

    [Export(PropertyHint.Range, "-180,180,0.5")]
    public float MuzzleFlashRotationDegrees { get; set; } = 90.0f;

    [Export]
    public bool MuzzleLightEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0.01,0.2,0.005")]
    public float MuzzleLightDurationSeconds { get; set; } = 0.045f;

    [Export(PropertyHint.Range, "0.0,8.0,0.05")]
    public float MuzzleLightEnergyMin { get; set; } = 1.65f;

    [Export(PropertyHint.Range, "0.0,8.0,0.05")]
    public float MuzzleLightEnergyMax { get; set; } = 2.25f;

    [Export(PropertyHint.Range, "0.1,8.0,0.05")]
    public float MuzzleLightTextureScale { get; set; } = 1.45f;

    [Export]
    public Color MuzzleLightColor { get; set; } = new(1.0f, 0.72f, 0.30f, 1.0f);

    [Export(PropertyHint.Range, "1,64,1")]
    public int BulletPoolSize { get; set; } = 12;

    [Export(PropertyHint.Range, "500,20000,100")]
    public float BulletVisualSpeed { get; set; } = 6500.0f;

    [Export(PropertyHint.Range, "0.01,0.5,0.005")]
    public float MinimumBulletLifetimeSeconds { get; set; } = 0.035f;

    [Export(PropertyHint.Range, "0.02,0.5,0.005")]
    public float MaximumBulletLifetimeSeconds { get; set; } = 0.15f;

    [Export(PropertyHint.Range, "0.25,6.0,0.25")]
    public float BulletVisualScale { get; set; } = 3.0f;

    [Export]
    public Color BulletColor { get; set; } = new(1.0f, 0.98f, 0.72f, 1.0f);

    [Export]
    public bool SmokeEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "4,96,1")]
    public int SmokePoolSize { get; set; } = 36;

    [Export(PropertyHint.Range, "0,6,1")]
    public int SmokeParticlesPerShot { get; set; } = 1;

    [Export(PropertyHint.Range, "0,6,1")]
    public int SmokeExtraParticlesAtMaxHeat { get; set; } = 2;

    [Export(PropertyHint.Range, "0.05,2.0,0.01")]
    public float SmokeLifetimeMinSeconds { get; set; } = 0.28f;

    [Export(PropertyHint.Range, "0.05,2.0,0.01")]
    public float SmokeLifetimeMaxSeconds { get; set; } = 0.58f;

    [Export(PropertyHint.Range, "0,200,1")]
    public float SmokeSpeedMin { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "0,200,1")]
    public float SmokeSpeedMax { get; set; } = 28.0f;

    [Export(PropertyHint.Range, "0,90,1")]
    public float SmokeSpreadDegrees { get; set; } = 34.0f;

    [Export(PropertyHint.Range, "0,20,0.25")]
    public float SmokeUpwardDrift { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "0,10,0.1")]
    public float SmokeDragPerSecond { get; set; } = 2.4f;

    [Export(PropertyHint.Range, "0.25,6.0,0.25")]
    public float SmokeStartScaleMin { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "0.25,6.0,0.25")]
    public float SmokeStartScaleMax { get; set; } = 1.75f;

    [Export(PropertyHint.Range, "1.0,4.0,0.05")]
    public float SmokeExpansionMultiplier { get; set; } = 1.8f;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float SmokeAlphaMin { get; set; } = 0.16f;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float SmokeAlphaMax { get; set; } = 0.30f;

    [Export]
    public Color SmokeColor { get; set; } = new(0.70f, 0.72f, 0.68f, 1.0f);

    [Export(PropertyHint.Range, "0.0,1.0,0.01")]
    public float HeatPerShot { get; set; } = 0.14f;

    [Export(PropertyHint.Range, "0.01,4.0,0.01")]
    public float HeatDecayPerSecond { get; set; } = 0.32f;

    [Export(PropertyHint.Range, "4,96,1")]
    public int ImpactPixelPoolSize { get; set; } = 40;

    [Export(PropertyHint.Range, "1,12,1")]
    public int ImpactPixelsPerHit { get; set; } = 5;

    [Export(PropertyHint.Range, "0.02,0.3,0.005")]
    public float ImpactPixelLifetimeSeconds { get; set; } = 0.075f;

    [Export(PropertyHint.Range, "0,6,0.25")]
    public float ImpactPixelScatterRadius { get; set; } = 2.5f;

    [Export(PropertyHint.Range, "1,6,1")]
    public int ImpactPixelScale { get; set; } = 2;

    public void Validate()
    {
        Texture2D? muzzleFlash = MuzzleFlashTexture;
        Texture2D bullet = BulletTexture
            ?? throw new InvalidOperationException(
                $"{nameof(WeaponFxProfile2D)} at '{GetDisplayPath()}' requires a bullet texture.");

        if (MuzzleFlashFrameCount <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MuzzleFlashFrameCount)} must be positive.");
        }

        if (UseProceduralMuzzleFlash && MuzzleFlashFrameCount != 4)
        {
            throw new InvalidOperationException(
                $"{nameof(UseProceduralMuzzleFlash)} currently requires exactly 4 muzzle-flash frames.");
        }

        if (!UseProceduralMuzzleFlash &&
            (muzzleFlash is null ||
             muzzleFlash.GetWidth() <= 0 ||
             muzzleFlash.GetHeight() <= 0 ||
             muzzleFlash.GetWidth() % MuzzleFlashFrameCount != 0))
        {
            throw new InvalidOperationException(
                $"{nameof(WeaponFxProfile2D)} at '{GetDisplayPath()}' requires a valid " +
                $"horizontal muzzle-flash sheet with {MuzzleFlashFrameCount} frames " +
                $"when procedural muzzle flash is disabled.");
        }

        if (bullet.GetWidth() <= 0 || bullet.GetHeight() <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(WeaponFxProfile2D)} at '{GetDisplayPath()}' requires a non-empty bullet texture.");
        }

        ValidatePositive(MuzzleFlashDurationSeconds, nameof(MuzzleFlashDurationSeconds));
        ValidatePositive(MuzzleFlashScale, nameof(MuzzleFlashScale));
        ValidateFinite(MuzzleFlashLocalOffset, nameof(MuzzleFlashLocalOffset));
        ValidateFinite(MuzzleFlashRotationDegrees, nameof(MuzzleFlashRotationDegrees));
        ValidateFiniteColor(MuzzleLightColor, nameof(MuzzleLightColor));
        ValidateFiniteColor(BulletColor, nameof(BulletColor));
        ValidatePositive(BulletVisualSpeed, nameof(BulletVisualSpeed));
        ValidatePositive(MinimumBulletLifetimeSeconds, nameof(MinimumBulletLifetimeSeconds));
        ValidateOrderedPositive(
            MinimumBulletLifetimeSeconds,
            MaximumBulletLifetimeSeconds,
            nameof(MinimumBulletLifetimeSeconds),
            nameof(MaximumBulletLifetimeSeconds));
        ValidatePositive(BulletVisualScale, nameof(BulletVisualScale));

        if (MuzzleLightEnabled)
        {
            ValidatePositive(MuzzleLightDurationSeconds, nameof(MuzzleLightDurationSeconds));
            ValidateNonNegative(MuzzleLightEnergyMin, nameof(MuzzleLightEnergyMin));
            ValidateOrderedNonNegative(
                MuzzleLightEnergyMin,
                MuzzleLightEnergyMax,
                nameof(MuzzleLightEnergyMin),
                nameof(MuzzleLightEnergyMax));
            ValidatePositive(MuzzleLightTextureScale, nameof(MuzzleLightTextureScale));
        }

        if (BulletPoolSize <= 0 ||
            ImpactPixelPoolSize <= 0 ||
            ImpactPixelsPerHit <= 0 ||
            ImpactPixelScale <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(WeaponFxProfile2D)} at '{GetDisplayPath()}' requires positive bullet and impact pools.");
        }

        ValidatePositive(ImpactPixelLifetimeSeconds, nameof(ImpactPixelLifetimeSeconds));
        ValidateNonNegative(ImpactPixelScatterRadius, nameof(ImpactPixelScatterRadius));

        if (SmokeEnabled)
        {
            if (SmokePoolSize <= 0 || SmokeParticlesPerShot < 0 || SmokeExtraParticlesAtMaxHeat < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(WeaponFxProfile2D)} at '{GetDisplayPath()}' has invalid smoke pool settings.");
            }

            ValidateOrderedPositive(
                SmokeLifetimeMinSeconds,
                SmokeLifetimeMaxSeconds,
                nameof(SmokeLifetimeMinSeconds),
                nameof(SmokeLifetimeMaxSeconds));
            ValidateOrderedNonNegative(
                SmokeSpeedMin,
                SmokeSpeedMax,
                nameof(SmokeSpeedMin),
                nameof(SmokeSpeedMax));
            ValidateNonNegative(SmokeSpreadDegrees, nameof(SmokeSpreadDegrees));
            ValidateNonNegative(SmokeUpwardDrift, nameof(SmokeUpwardDrift));
            ValidateNonNegative(SmokeDragPerSecond, nameof(SmokeDragPerSecond));
            ValidateOrderedPositive(
                SmokeStartScaleMin,
                SmokeStartScaleMax,
                nameof(SmokeStartScaleMin),
                nameof(SmokeStartScaleMax));
            ValidatePositive(SmokeExpansionMultiplier, nameof(SmokeExpansionMultiplier));
            ValidateOrderedPositive(
                SmokeAlphaMin,
                SmokeAlphaMax,
                nameof(SmokeAlphaMin),
                nameof(SmokeAlphaMax));
            if (SmokeAlphaMax > 1.0f)
            {
                throw new InvalidOperationException(
                    $"{nameof(SmokeAlphaMax)} cannot exceed 1.0.");
            }

            ValidateFiniteColor(SmokeColor, nameof(SmokeColor));
            ValidateNonNegative(HeatPerShot, nameof(HeatPerShot));
            if (HeatPerShot > 1.0f)
            {
                throw new InvalidOperationException(
                    $"{nameof(HeatPerShot)} cannot exceed 1.0.");
            }

            ValidatePositive(HeatDecayPerSecond, nameof(HeatDecayPerSecond));
        }

    }

    private static void ValidatePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new InvalidOperationException($"{name} must be finite and positive.");
        }
    }

    private static void ValidateNonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0.0f)
        {
            throw new InvalidOperationException($"{name} must be finite and non-negative.");
        }
    }

    private static void ValidateOrderedPositive(
        float min,
        float max,
        string minName,
        string maxName)
    {
        ValidatePositive(min, minName);
        ValidatePositive(max, maxName);
        if (min > max)
        {
            throw new InvalidOperationException($"{minName} cannot exceed {maxName}.");
        }
    }

    private static void ValidateOrderedNonNegative(
        float min,
        float max,
        string minName,
        string maxName)
    {
        ValidateNonNegative(min, minName);
        ValidateNonNegative(max, maxName);
        if (min > max)
        {
            throw new InvalidOperationException($"{minName} cannot exceed {maxName}.");
        }
    }

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidOperationException($"{name} must be finite.");
        }
    }

    private static void ValidateFinite(Vector2 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new InvalidOperationException($"{name} must contain finite values.");
        }
    }

    private static void ValidateFiniteColor(Color value, string name)
    {
        if (!float.IsFinite(value.R) ||
            !float.IsFinite(value.G) ||
            !float.IsFinite(value.B) ||
            !float.IsFinite(value.A))
        {
            throw new InvalidOperationException($"{name} must contain finite values.");
        }
    }

    private string GetDisplayPath()
    {
        return string.IsNullOrWhiteSpace(ResourcePath) ? "<unsaved resource>" : ResourcePath;
    }
}
