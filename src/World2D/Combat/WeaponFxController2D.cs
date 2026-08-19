using System;
using Godot;

namespace LineZero.World2D.Combat;

public enum WeaponImpactFxKind
{
    None = 0,
    Obstacle = 1,
}

public sealed partial class WeaponFxController2D : Node2D
{
    private const float MinimumShotDistanceSquared = 0.0001f;
    private const int MuzzleLightTextureSize = 64;
    private const int ProceduralMuzzleFlashVariantCount = 4;
    private const int ProceduralMuzzleFlashFrameWidth = 24;
    private const int ProceduralMuzzleFlashFrameHeight = 16;

    private sealed class BulletVisualState
    {
        public Sprite2D Sprite { get; init; } = null!;
        public Vector2 Start { get; set; }
        public Vector2 End { get; set; }
        public float ElapsedSeconds { get; set; }
        public float DurationSeconds { get; set; }
        public WeaponImpactFxKind ImpactKind { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class SmokeVisualState
    {
        public Sprite2D Sprite { get; init; } = null!;
        public Vector2 Velocity { get; set; }
        public float AngularVelocity { get; set; }
        public float StartScale { get; set; }
        public float EndScale { get; set; }
        public float StartAlpha { get; set; }
        public float ElapsedSeconds { get; set; }
        public float DurationSeconds { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class ImpactPixelState
    {
        public Sprite2D Sprite { get; init; } = null!;
        public float ElapsedSeconds { get; set; }
        public float DurationSeconds { get; set; }
        public bool IsActive { get; set; }
    }

    private WeaponFxProfile2D _profile = null!;
    private Sprite2D _weaponSprite = null!;
    private Marker2D _muzzlePoint = null!;
    private Sprite2D _muzzleFlashSprite = null!;
    private PointLight2D _muzzleFlashLight = null!;

    private BulletVisualState[] _bulletPool = Array.Empty<BulletVisualState>();
    private SmokeVisualState[] _smokePool = Array.Empty<SmokeVisualState>();
    private ImpactPixelState[] _impactPixelPool = Array.Empty<ImpactPixelState>();

    private ImageTexture _impactPixelTexture = null!;
    private ImageTexture _smokeTexture = null!;
    private ImageTexture _muzzleLightTexture = null!;
    private ImageTexture[] _muzzleFlashVariants = Array.Empty<ImageTexture>();

    private readonly RandomNumberGenerator _random = new();

    private int _nextBulletIndex;
    private int _nextSmokeIndex;
    private int _nextImpactPixelIndex;

    private float _muzzleFlashElapsedSeconds;
    private float _muzzleLightElapsedSeconds;
    private float _muzzleLightPeakEnergy;
    private float _heat;

    [Export]
    public WeaponFxProfile2D? Profile { get; set; }

    public float Heat01 => _heat;

    public bool HasActiveShotVisuals
    {
        get
        {
            if (GodotObject.IsInstanceValid(_muzzleFlashSprite) && _muzzleFlashSprite.Visible)
            {
                return true;
            }

            if (GodotObject.IsInstanceValid(_muzzleFlashLight) && _muzzleFlashLight.Enabled)
            {
                return true;
            }

            return HasActive(_bulletPool) ||
                   HasActive(_smokePool) ||
                   HasActive(_impactPixelPool);
        }
    }

    public override void _Ready()
    {
        _profile = Profile
            ?? throw new InvalidOperationException(
                $"{nameof(WeaponFxController2D)} on '{Name}' requires an FX profile.");
        _profile.Validate();

        _weaponSprite = RequireNode<Sprite2D>("%WeaponSprite");
        _muzzlePoint = RequireNode<Marker2D>("%MuzzlePoint");
        _muzzleFlashSprite = RequireNode<Sprite2D>("%MuzzleFlashSprite");
        _muzzleFlashLight = RequireNode<PointLight2D>("%MuzzleFlashLight");

        _random.Randomize();

        CreateSharedTextures();
        ConfigureAuthoredNodes();
        CreatePools();

        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        float deltaSeconds = double.IsFinite(delta) && delta > 0.0
            ? (float)delta
            : 0.0f;
        if (deltaSeconds <= 0.0f)
        {
            return;
        }

        UpdateHeat(deltaSeconds);
        UpdateMuzzleFlash(deltaSeconds);
        UpdateMuzzleLight(deltaSeconds);
        UpdateBullets(deltaSeconds);
        UpdateSmoke(deltaSeconds);
        UpdateImpactPixels(deltaSeconds);

        if (_heat <= 0.0f && !HasActiveShotVisuals)
        {
            SetProcess(false);
        }
    }

    public void PlayShot(
        Vector2 worldStart,
        Vector2 worldEnd,
        WeaponImpactFxKind impactKind = WeaponImpactFxKind.None)
    {
        if (!IsFinite(worldStart) || !IsFinite(worldEnd))
        {
            throw new ArgumentException("Weapon FX requires finite shot positions.");
        }

        SetProcess(true);

        if (_profile.SmokeEnabled)
        {
            _heat = Mathf.Clamp(_heat + _profile.HeatPerShot, 0.0f, 1.0f);
        }

        Vector2 displacement = worldEnd - worldStart;
        Vector2 shotDirection = displacement.LengthSquared() > MinimumShotDistanceSquared
            ? displacement.Normalized()
            : Vector2.Right.Rotated(_muzzlePoint.GlobalRotation).Normalized();

        PlayMuzzleFlash(worldStart, shotDirection);
        PlayMuzzleLight();
        SpawnSmoke(worldStart, shotDirection);

        if (displacement.LengthSquared() <= MinimumShotDistanceSquared)
        {
            if (impactKind == WeaponImpactFxKind.Obstacle)
            {
                PlayObstacleImpact(worldEnd);
            }

            return;
        }

        StartBullet(worldStart, worldEnd, impactKind);
    }

    public void StopAll()
    {
        if (GodotObject.IsInstanceValid(_muzzleFlashSprite))
        {
            _muzzleFlashSprite.Visible = false;
            _muzzleFlashSprite.Frame = 0;
        }

        if (GodotObject.IsInstanceValid(_muzzleFlashLight))
        {
            _muzzleFlashLight.Enabled = false;
            _muzzleFlashLight.Energy = 0.0f;
        }

        _muzzleFlashElapsedSeconds = 0.0f;
        _muzzleLightElapsedSeconds = 0.0f;
        _muzzleLightPeakEnergy = 0.0f;
        _heat = 0.0f;

        StopPool(_bulletPool);
        StopPool(_smokePool);
        StopPool(_impactPixelPool);

        SetProcess(false);
    }

    private void ConfigureAuthoredNodes()
    {
        Texture2D muzzleFlashTexture;
        if (_profile.UseProceduralMuzzleFlash)
        {
            if (_muzzleFlashVariants.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(WeaponFxController2D)} failed to create procedural muzzle-flash variants.");
            }

            muzzleFlashTexture = _muzzleFlashVariants[0];
            _muzzleFlashSprite.Centered = false;
            _muzzleFlashSprite.Offset = new Vector2(
                0.0f,
                -ProceduralMuzzleFlashFrameHeight * 0.5f);
        }
        else
        {
            muzzleFlashTexture = _profile.MuzzleFlashTexture
                ?? throw new InvalidOperationException(
                    "Validated muzzle-flash texture became unavailable.");
            _muzzleFlashSprite.Centered = true;
            _muzzleFlashSprite.Offset = Vector2.Zero;
        }

        _muzzleFlashSprite.Texture = muzzleFlashTexture;
        _muzzleFlashSprite.Hframes = _profile.MuzzleFlashFrameCount;
        _muzzleFlashSprite.Vframes = 1;
        _muzzleFlashSprite.Frame = 0;
        _muzzleFlashSprite.Scale = Vector2.One * _profile.MuzzleFlashScale;
        _muzzleFlashSprite.Visible = false;

        float weaponScaleX = Mathf.Abs(_weaponSprite.Scale.X);
        float weaponScaleY = Mathf.Abs(_weaponSprite.Scale.Y);
        if (!float.IsFinite(weaponScaleX) ||
            !float.IsFinite(weaponScaleY) ||
            weaponScaleX <= 0.0f ||
            weaponScaleY <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(WeaponFxController2D)} requires a finite non-zero weapon scale.");
        }

        _muzzleFlashLight.Scale = new Vector2(
            1.0f / weaponScaleX,
            1.0f / weaponScaleY);
        _muzzleFlashLight.Color = _profile.MuzzleLightColor;
        _muzzleFlashLight.TextureScale = _profile.MuzzleLightTextureScale;
        _muzzleFlashLight.Energy = 0.0f;
        _muzzleFlashLight.Enabled = false;
    }

    private void CreateSharedTextures()
    {
        _impactPixelTexture = CreateImpactPixelTexture();
        _smokeTexture = CreateSmokeTexture();
        _muzzleLightTexture = CreateRadialLightTexture(MuzzleLightTextureSize);
        _muzzleFlashLight.Texture = _muzzleLightTexture;

        if (_profile.UseProceduralMuzzleFlash)
        {
            _muzzleFlashVariants = CreateProceduralMuzzleFlashVariants();
        }
    }

    private void CreatePools()
    {
        Texture2D bulletTexture = _profile.BulletTexture
            ?? throw new InvalidOperationException("Validated bullet texture became unavailable.");
        Vector2 bulletTextureOffset = ResolveOpaqueContentCenteringOffset(bulletTexture);

        _bulletPool = new BulletVisualState[_profile.BulletPoolSize];
        for (int i = 0; i < _bulletPool.Length; i++)
        {
            Sprite2D sprite = CreateWorldSprite(
                $"BulletVisual{i}",
                bulletTexture,
                zIndex: 3000,
                scale: _profile.BulletVisualScale,
                color: _profile.BulletColor);
            sprite.Offset = bulletTextureOffset;

            _bulletPool[i] = new BulletVisualState
            {
                Sprite = sprite,
            };
        }

        if (_profile.SmokeEnabled)
        {
            _smokePool = new SmokeVisualState[_profile.SmokePoolSize];
            for (int i = 0; i < _smokePool.Length; i++)
            {
                Sprite2D sprite = CreateWorldSprite(
                    $"MuzzleSmoke{i}",
                    _smokeTexture,
                    zIndex: 2999,
                    scale: 1.0f,
                    color: _profile.SmokeColor);

                _smokePool[i] = new SmokeVisualState
                {
                    Sprite = sprite,
                };
            }
        }

        _impactPixelPool = new ImpactPixelState[_profile.ImpactPixelPoolSize];
        for (int i = 0; i < _impactPixelPool.Length; i++)
        {
            Sprite2D sprite = CreateWorldSprite(
                $"ImpactPixel{i}",
                _impactPixelTexture,
                zIndex: 3001,
                scale: _profile.ImpactPixelScale,
                color: Colors.White);

            _impactPixelPool[i] = new ImpactPixelState
            {
                Sprite = sprite,
            };
        }
    }

    private Sprite2D CreateWorldSprite(
        string nodeName,
        Texture2D texture,
        int zIndex,
        float scale,
        Color color)
    {
        Sprite2D sprite = new()
        {
            Name = nodeName,
            Texture = texture,
            Centered = true,
            Visible = false,
            ZIndex = zIndex,
            Scale = Vector2.One * scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            LightMask = 1,
            Modulate = color,
        };
        AddChild(sprite);
        // These pooled FX are driven in _Process() and can teleport between reused
        // world positions. Disable physics interpolation on the branch in the scene
        // and reset here as a second line of defense against one-frame streaking.
        sprite.ResetPhysicsInterpolation();
        return sprite;
    }

    private void StartBullet(
        Vector2 worldStart,
        Vector2 worldEnd,
        WeaponImpactFxKind impactKind)
    {
        BulletVisualState state = AcquireBulletVisual();
        Vector2 displacement = worldEnd - worldStart;
        float distance = displacement.Length();
        float duration = Mathf.Clamp(
            distance / _profile.BulletVisualSpeed,
            _profile.MinimumBulletLifetimeSeconds,
            _profile.MaximumBulletLifetimeSeconds);

        state.Start = worldStart;
        state.End = worldEnd;
        state.ElapsedSeconds = 0.0f;
        state.DurationSeconds = duration;
        state.ImpactKind = impactKind;
        state.IsActive = true;

        Sprite2D sprite = state.Sprite;
        sprite.GlobalPosition = worldStart;
        sprite.GlobalRotation = displacement.Angle() - Mathf.Pi * 0.5f;
        sprite.Modulate = _profile.BulletColor;
        sprite.ResetPhysicsInterpolation();
        sprite.Visible = true;
    }

    private void PlayMuzzleFlash(Vector2 worldStart, Vector2 shotDirection)
    {
        if (_profile.UseProceduralMuzzleFlash && _muzzleFlashVariants.Length > 0)
        {
            int variantIndex = _random.RandiRange(0, _muzzleFlashVariants.Length - 1);
            _muzzleFlashSprite.Texture = _muzzleFlashVariants[variantIndex];
        }

        Vector2 direction = shotDirection.LengthSquared() > MinimumShotDistanceSquared
            ? shotDirection.Normalized()
            : Vector2.Right;
        Vector2 perpendicular = new(-direction.Y, direction.X);
        Vector2 offset =
            direction * _profile.MuzzleFlashLocalOffset.X +
            perpendicular * _profile.MuzzleFlashLocalOffset.Y;

        _muzzleFlashSprite.GlobalPosition = worldStart + offset;
        _muzzleFlashSprite.GlobalRotation =
            direction.Angle() + Mathf.DegToRad(_profile.MuzzleFlashRotationDegrees);
        _muzzleFlashSprite.ResetPhysicsInterpolation();
        _muzzleFlashElapsedSeconds = 0.0f;
        _muzzleFlashSprite.Frame = 0;
        _muzzleFlashSprite.Visible = true;
    }

    private void PlayMuzzleLight()
    {
        if (!_profile.MuzzleLightEnabled)
        {
            return;
        }

        _muzzleLightElapsedSeconds = 0.0f;
        _muzzleLightPeakEnergy = _random.RandfRange(
            _profile.MuzzleLightEnergyMin,
            _profile.MuzzleLightEnergyMax);
        _muzzleFlashLight.Energy = _muzzleLightPeakEnergy;
        _muzzleFlashLight.Enabled = true;
    }

    private void SpawnSmoke(Vector2 worldStart, Vector2 shotDirection)
    {
        if (!_profile.SmokeEnabled || _smokePool.Length == 0)
        {
            return;
        }

        int extraParticles = (int)Mathf.Floor(
            _heat * _profile.SmokeExtraParticlesAtMaxHeat + 0.0001f);
        int particleCount = _profile.SmokeParticlesPerShot + extraParticles;

        for (int i = 0; i < particleCount; i++)
        {
            SmokeVisualState state = AcquireSmokeVisual();
            float spreadRadians = Mathf.DegToRad(_profile.SmokeSpreadDegrees);
            float angleOffset = _random.RandfRange(-spreadRadians, spreadRadians);
            Vector2 direction = shotDirection.Rotated(angleOffset).Normalized();
            float speed = _random.RandfRange(_profile.SmokeSpeedMin, _profile.SmokeSpeedMax);

            state.Velocity =
                direction * speed +
                Vector2.Up * _random.RandfRange(
                    _profile.SmokeUpwardDrift * 0.4f,
                    _profile.SmokeUpwardDrift);
            state.AngularVelocity = _random.RandfRange(-1.4f, 1.4f);
            state.StartScale = _random.RandfRange(
                _profile.SmokeStartScaleMin,
                _profile.SmokeStartScaleMax);
            state.EndScale = state.StartScale * _profile.SmokeExpansionMultiplier;
            state.StartAlpha = _random.RandfRange(
                _profile.SmokeAlphaMin,
                _profile.SmokeAlphaMax);
            state.ElapsedSeconds = 0.0f;
            state.DurationSeconds = _random.RandfRange(
                _profile.SmokeLifetimeMinSeconds,
                _profile.SmokeLifetimeMaxSeconds);
            state.IsActive = true;

            Sprite2D sprite = state.Sprite;
            sprite.GlobalPosition = worldStart + RandomPointInCircle(1.5f);
            sprite.GlobalRotation = _random.RandfRange(0.0f, Mathf.Tau);
            sprite.Scale = Vector2.One * state.StartScale;
            sprite.Modulate = WithAlpha(_profile.SmokeColor, state.StartAlpha);
            sprite.ResetPhysicsInterpolation();
            sprite.Visible = true;
        }
    }

    private void UpdateHeat(float deltaSeconds)
    {
        if (_heat <= 0.0f)
        {
            _heat = 0.0f;
            return;
        }

        _heat = Mathf.Max(
            0.0f,
            _heat - _profile.HeatDecayPerSecond * deltaSeconds);
    }

    private void UpdateMuzzleFlash(float deltaSeconds)
    {
        if (!_muzzleFlashSprite.Visible)
        {
            return;
        }

        _muzzleFlashElapsedSeconds += deltaSeconds;
        if (_muzzleFlashElapsedSeconds >= _profile.MuzzleFlashDurationSeconds)
        {
            _muzzleFlashSprite.Visible = false;
            _muzzleFlashSprite.Frame = 0;
            return;
        }

        float normalized = _muzzleFlashElapsedSeconds / _profile.MuzzleFlashDurationSeconds;
        int frame = Math.Clamp(
            (int)Mathf.Floor(normalized * _profile.MuzzleFlashFrameCount),
            0,
            _profile.MuzzleFlashFrameCount - 1);
        _muzzleFlashSprite.Frame = frame;
    }

    private void UpdateMuzzleLight(float deltaSeconds)
    {
        if (!_muzzleFlashLight.Enabled)
        {
            return;
        }

        _muzzleLightElapsedSeconds += deltaSeconds;
        float progress = Mathf.Clamp(
            _muzzleLightElapsedSeconds / _profile.MuzzleLightDurationSeconds,
            0.0f,
            1.0f);

        float remaining = 1.0f - SmoothStep01(progress);
        _muzzleFlashLight.Energy = _muzzleLightPeakEnergy * remaining * remaining;

        if (progress < 1.0f)
        {
            return;
        }

        _muzzleFlashLight.Energy = 0.0f;
        _muzzleFlashLight.Enabled = false;
    }

    private void UpdateBullets(float deltaSeconds)
    {
        foreach (BulletVisualState state in _bulletPool)
        {
            if (!state.IsActive)
            {
                continue;
            }

            state.ElapsedSeconds += deltaSeconds;
            float progress = Mathf.Clamp(
                state.ElapsedSeconds / state.DurationSeconds,
                0.0f,
                1.0f);

            state.Sprite.GlobalPosition = state.Start.Lerp(state.End, progress);
            state.Sprite.Modulate = _profile.BulletColor;

            if (progress < 1.0f)
            {
                continue;
            }

            state.Sprite.GlobalPosition = state.End;
            state.Sprite.Visible = false;
            state.IsActive = false;

            WeaponImpactFxKind impactKind = state.ImpactKind;
            state.ImpactKind = WeaponImpactFxKind.None;
            if (impactKind == WeaponImpactFxKind.Obstacle)
            {
                PlayObstacleImpact(state.End);
            }
        }
    }

    private void UpdateSmoke(float deltaSeconds)
    {
        if (_smokePool.Length == 0)
        {
            return;
        }

        float dragMultiplier = Mathf.Exp(-_profile.SmokeDragPerSecond * deltaSeconds);

        foreach (SmokeVisualState state in _smokePool)
        {
            if (!state.IsActive)
            {
                continue;
            }

            state.ElapsedSeconds += deltaSeconds;
            float progress = Mathf.Clamp(
                state.ElapsedSeconds / state.DurationSeconds,
                0.0f,
                1.0f);

            state.Sprite.GlobalPosition += state.Velocity * deltaSeconds;
            state.Velocity *= dragMultiplier;
            state.Sprite.GlobalRotation += state.AngularVelocity * deltaSeconds;

            float eased = SmoothStep01(progress);
            float scale = Mathf.Lerp(state.StartScale, state.EndScale, eased);
            state.Sprite.Scale = Vector2.One * scale;

            float alpha = state.StartAlpha * Mathf.Pow(1.0f - progress, 1.35f);
            state.Sprite.Modulate = WithAlpha(_profile.SmokeColor, alpha);

            if (progress < 1.0f)
            {
                continue;
            }

            state.IsActive = false;
            state.Sprite.Visible = false;
        }
    }

    private void PlayObstacleImpact(Vector2 worldPosition)
    {
        for (int i = 0; i < _profile.ImpactPixelsPerHit; i++)
        {
            ImpactPixelState state = AcquireImpactPixel();
            Vector2 offset = RandomPointInCircle(_profile.ImpactPixelScatterRadius);

            state.Sprite.GlobalPosition = worldPosition + offset;
            state.Sprite.Modulate = new Color(
                1.0f,
                _random.RandfRange(0.80f, 0.96f),
                _random.RandfRange(0.10f, 0.28f),
                1.0f);
            state.Sprite.ResetPhysicsInterpolation();
            state.Sprite.Visible = true;
            state.ElapsedSeconds = 0.0f;
            state.DurationSeconds = _profile.ImpactPixelLifetimeSeconds;
            state.IsActive = true;
        }
    }

    private void UpdateImpactPixels(float deltaSeconds)
    {
        foreach (ImpactPixelState state in _impactPixelPool)
        {
            if (!state.IsActive)
            {
                continue;
            }

            state.ElapsedSeconds += deltaSeconds;
            float progress = Mathf.Clamp(
                state.ElapsedSeconds / state.DurationSeconds,
                0.0f,
                1.0f);

            Color color = state.Sprite.Modulate;
            state.Sprite.Modulate = new Color(
                color.R,
                color.G,
                color.B,
                1.0f - progress);

            if (progress < 1.0f)
            {
                continue;
            }

            state.IsActive = false;
            state.Sprite.Visible = false;
        }
    }

    private BulletVisualState AcquireBulletVisual()
    {
        for (int offset = 0; offset < _bulletPool.Length; offset++)
        {
            int index = (_nextBulletIndex + offset) % _bulletPool.Length;
            BulletVisualState candidate = _bulletPool[index];
            if (!candidate.IsActive)
            {
                _nextBulletIndex = (index + 1) % _bulletPool.Length;
                candidate.ImpactKind = WeaponImpactFxKind.None;
                return candidate;
            }
        }

        BulletVisualState reused = _bulletPool[_nextBulletIndex];
        _nextBulletIndex = (_nextBulletIndex + 1) % _bulletPool.Length;
        reused.IsActive = false;
        reused.ImpactKind = WeaponImpactFxKind.None;
        reused.Sprite.Visible = false;
        return reused;
    }

    private SmokeVisualState AcquireSmokeVisual()
    {
        for (int offset = 0; offset < _smokePool.Length; offset++)
        {
            int index = (_nextSmokeIndex + offset) % _smokePool.Length;
            SmokeVisualState candidate = _smokePool[index];
            if (!candidate.IsActive)
            {
                _nextSmokeIndex = (index + 1) % _smokePool.Length;
                return candidate;
            }
        }

        SmokeVisualState reused = _smokePool[_nextSmokeIndex];
        _nextSmokeIndex = (_nextSmokeIndex + 1) % _smokePool.Length;
        reused.IsActive = false;
        reused.Sprite.Visible = false;
        return reused;
    }

    private ImpactPixelState AcquireImpactPixel()
    {
        for (int offset = 0; offset < _impactPixelPool.Length; offset++)
        {
            int index = (_nextImpactPixelIndex + offset) % _impactPixelPool.Length;
            ImpactPixelState candidate = _impactPixelPool[index];
            if (!candidate.IsActive)
            {
                _nextImpactPixelIndex = (index + 1) % _impactPixelPool.Length;
                return candidate;
            }
        }

        ImpactPixelState reused = _impactPixelPool[_nextImpactPixelIndex];
        _nextImpactPixelIndex = (_nextImpactPixelIndex + 1) % _impactPixelPool.Length;
        reused.IsActive = false;
        reused.Sprite.Visible = false;
        return reused;
    }

    private Vector2 RandomPointInCircle(float radius)
    {
        if (radius <= 0.0f)
        {
            return Vector2.Zero;
        }

        float angle = _random.RandfRange(0.0f, Mathf.Tau);
        float distance = radius * Mathf.Sqrt(_random.Randf());
        return Vector2.Right.Rotated(angle) * distance;
    }

    private static Vector2 ResolveOpaqueContentCenteringOffset(Texture2D texture)
    {
        Image image = texture.GetImage();
        if (image is null || image.IsEmpty())
        {
            return Vector2.Zero;
        }

        int minX = image.GetWidth();
        int minY = image.GetHeight();
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A <= 0.01f)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return Vector2.Zero;
        }

        Vector2 contentCenter = new(
            (minX + maxX + 1.0f) * 0.5f,
            (minY + maxY + 1.0f) * 0.5f);
        Vector2 textureCenter = texture.GetSize() * 0.5f;
        return textureCenter - contentCenter;
    }

    private static ImageTexture[] CreateProceduralMuzzleFlashVariants()
    {
        ImageTexture[] variants = new ImageTexture[ProceduralMuzzleFlashVariantCount];
        for (int variant = 0; variant < variants.Length; variant++)
        {
            Image sheet = Image.Create(
                ProceduralMuzzleFlashFrameWidth * 4,
                ProceduralMuzzleFlashFrameHeight,
                false,
                Image.Format.Rgba8);
            sheet.Fill(Colors.Transparent);

            for (int frame = 0; frame < 4; frame++)
            {
                DrawProceduralMuzzleFlashFrame(sheet, frame, variant);
            }

            variants[variant] = ImageTexture.CreateFromImage(sheet);
        }

        return variants;
    }

    private static void DrawProceduralMuzzleFlashFrame(
        Image sheet,
        int frameIndex,
        int variantIndex)
    {
        int[] baseLengths = { 23, 18, 12, 7 };
        int[] baseHalfWidths = { 6, 5, 4, 2 };

        int lengthVariation = ((variantIndex * 5 + frameIndex * 3) % 5) - 2;
        int widthVariation = ((variantIndex * 3 + frameIndex) % 3) - 1;

        int length = Math.Clamp(
            baseLengths[frameIndex] + lengthVariation,
            4,
            ProceduralMuzzleFlashFrameWidth);
        int maxHalfWidth = Math.Clamp(
            baseHalfWidths[frameIndex] + widthVariation,
            2,
            ProceduralMuzzleFlashFrameHeight / 2 - 1);

        int originX = frameIndex * ProceduralMuzzleFlashFrameWidth;
        int centerY = ProceduralMuzzleFlashFrameHeight / 2;

        for (int x = 0; x < length; x++)
        {
            float t = length <= 1 ? 1.0f : x / (float)(length - 1);
            float bulge = Mathf.Sin(Mathf.Clamp(t * 1.25f, 0.0f, 1.0f) * Mathf.Pi);
            float taper = 1.0f - Mathf.Pow(t, 1.55f);
            int wobble = ((x + variantIndex * 2 + frameIndex) % 5 == 0) ? 1 : 0;
            int halfWidth = Math.Clamp(
                1 + Mathf.RoundToInt(maxHalfWidth * bulge * taper) + wobble,
                1,
                maxHalfWidth);

            for (int y = -halfWidth; y <= halfWidth; y++)
            {
                float radial = Mathf.Abs(y) / (float)Math.Max(halfWidth, 1);
                Color color;
                if (radial <= 0.22f && t <= 0.58f)
                {
                    color = new Color(1.0f, 0.98f, 0.78f, 1.0f);
                }
                else if (radial <= 0.62f)
                {
                    color = new Color(1.0f, 0.76f, 0.12f, 1.0f);
                }
                else
                {
                    color = new Color(1.0f, 0.36f, 0.04f, 0.92f);
                }

                int pixelX = originX + x;
                int pixelY = centerY + y;
                if (pixelY >= 0 && pixelY < ProceduralMuzzleFlashFrameHeight)
                {
                    sheet.SetPixel(pixelX, pixelY, color);
                }
            }
        }

        int rayLength = Math.Min(length - 1, 8 + variantIndex);
        if (frameIndex <= 1 && rayLength > 2)
        {
            int rayY = centerY + (variantIndex % 2 == 0 ? -1 : 1) * (3 + frameIndex);
            for (int x = 2; x <= rayLength; x += 2)
            {
                if (rayY >= 0 && rayY < ProceduralMuzzleFlashFrameHeight)
                {
                    sheet.SetPixel(
                        originX + x,
                        rayY,
                        new Color(1.0f, 0.70f, 0.08f, 0.88f));
                }
            }
        }

        sheet.SetPixel(originX, centerY, new Color(1.0f, 0.98f, 0.82f, 1.0f));
        if (centerY - 1 >= 0)
        {
            sheet.SetPixel(originX + 1, centerY - 1, new Color(1.0f, 0.82f, 0.20f, 1.0f));
        }
        if (centerY + 1 < ProceduralMuzzleFlashFrameHeight)
        {
            sheet.SetPixel(originX + 1, centerY + 1, new Color(1.0f, 0.82f, 0.20f, 1.0f));
        }
    }

    private static ImageTexture CreateImpactPixelTexture()
    {
        Image image = Image.Create(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture CreateSmokeTexture()
    {
        Image image = Image.Create(5, 5, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);

        Color solid = Colors.White;
        Color soft = new(1.0f, 1.0f, 1.0f, 0.68f);

        image.SetPixel(2, 1, soft);
        image.SetPixel(1, 2, soft);
        image.SetPixel(2, 2, solid);
        image.SetPixel(3, 2, soft);
        image.SetPixel(2, 3, soft);
        image.SetPixel(3, 3, new Color(1.0f, 1.0f, 1.0f, 0.42f));

        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture CreateRadialLightTexture(int size)
    {
        Image image = Image.Create(size, size, false, Image.Format.Rgba8);
        float center = (size - 1) * 0.5f;
        float radius = Math.Max(center, 1.0f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / radius;
                float dy = (y - center) / radius;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float normalized = Mathf.Clamp(1.0f - distance, 0.0f, 1.0f);
                float intensity = normalized * normalized * (3.0f - 2.0f * normalized);
                intensity *= intensity;

                image.SetPixel(
                    x,
                    y,
                    new Color(intensity, intensity, intensity, intensity));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.R, color.G, color.B, alpha);
    }

    private static float SmoothStep01(float value)
    {
        float clamped = Mathf.Clamp(value, 0.0f, 1.0f);
        return clamped * clamped * (3.0f - 2.0f * clamped);
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }

    private static bool HasActive(BulletVisualState[] states)
    {
        foreach (BulletVisualState state in states)
        {
            if (state.IsActive)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasActive(SmokeVisualState[] states)
    {
        foreach (SmokeVisualState state in states)
        {
            if (state.IsActive)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasActive(ImpactPixelState[] states)
    {
        foreach (ImpactPixelState state in states)
        {
            if (state.IsActive)
            {
                return true;
            }
        }

        return false;
    }

    private static void StopPool(BulletVisualState[] states)
    {
        foreach (BulletVisualState state in states)
        {
            state.IsActive = false;
            state.ImpactKind = WeaponImpactFxKind.None;
            state.Sprite.Visible = false;
        }
    }

    private static void StopPool(SmokeVisualState[] states)
    {
        foreach (SmokeVisualState state in states)
        {
            state.IsActive = false;
            state.Sprite.Visible = false;
        }
    }

    private static void StopPool(ImpactPixelState[] states)
    {
        foreach (ImpactPixelState state in states)
        {
            state.IsActive = false;
            state.Sprite.Visible = false;
        }
    }

    private TNode RequireNode<TNode>(string path) where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(WeaponFxController2D)} on '{Name}' requires '{path}'.");
    }
}
