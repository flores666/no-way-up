using System;
using System.Collections.Generic;
using Godot;
using LineZero.Gameplay.Movement;

namespace LineZero.World2D.Presentation;

public sealed partial class PlayerMiniDayzPresentation2D : Node2D
{
    private const int SourceFrameWidth = 64;
    private const int SourceFrameHeight = 32;
    private const int RunFrameCount = 6;
    private const int IdleFrame = 0;
    private const float MinimumAnimationSpeed = 4.0f;
    private const float ShadowAlphaThreshold = 0.08f;
    private const int MaximumShadowRowStep = 2;

    private enum FacingSide
    {
        Left,
        Right
    }

    private PlayerController2D _player = null!;
    private Node2D _aimPivot = null!;
    private Sprite2D _characterSprite = null!;
    private Sprite2D _weaponSprite = null!;
    private LightOccluder2D _muzzleSelfShadowOccluder = null!;
    private bool _isRunning;
    private FacingSide _facingSide = FacingSide.Right;
    private float _frameCursor;
    private Vector2 _weaponBaseScale;
    private Vector2[][] _shadowPolygons = Array.Empty<Vector2[]>();
    private int _lastShadowFrame = -1;
    private FacingSide? _lastShadowFacingSide;

    [Export]
    public Texture2D? CharacterTexture { get; set; }

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float WalkFramesPerSecond { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float SprintFramesPerSecond { get; set; } = 12.0f;

    public override void _Ready()
    {
        _player = GetParent() as PlayerController2D
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} on '{Name}' requires a {nameof(PlayerController2D)} parent.");
        _aimPivot = RequireNode<Node2D>("%AimPivot");
        _characterSprite = RequireNode<Sprite2D>("%CharacterSprite");
        _weaponSprite = RequireNode<Sprite2D>("%WeaponSprite");
        _muzzleSelfShadowOccluder = RequireNode<LightOccluder2D>("%MuzzleSelfShadowOccluder");

        ValidateTexture(CharacterTexture, nameof(CharacterTexture), RunFrameCount);
        ValidateTexture(_weaponSprite.Texture, "WeaponSprite.Texture", expectedFrames: 1);
        ValidateFramesPerSecond(WalkFramesPerSecond, nameof(WalkFramesPerSecond));
        ValidateFramesPerSecond(SprintFramesPerSecond, nameof(SprintFramesPerSecond));

        _characterSprite.Texture = CharacterTexture;
        _characterSprite.Hframes = RunFrameCount;
        _characterSprite.Vframes = 1;
        _weaponBaseScale = new Vector2(
            Mathf.Abs(_weaponSprite.Scale.X),
            Mathf.Abs(_weaponSprite.Scale.Y));
        _weaponSprite.ZAsRelative = true;
        _weaponSprite.FlipH = false;
        _weaponSprite.FlipV = false;

        BuildDynamicShadowPolygons();
        ConfigureMuzzleSelfShadowOccluder();

        SetRunning(isRunning: false, restart: true);
        ApplyFacingSide(ResolveAimSide());
        UpdateDynamicShadowOccluder(force: true);
    }

    public override void _PhysicsProcess(double delta)
    {
        FacingSide facingSide = ResolveAimSide();
        if (facingSide != _facingSide)
        {
            ApplyFacingSide(facingSide);
        }

        float deltaSeconds = double.IsFinite(delta) && delta > 0.0
            ? (float)delta
            : 0.0f;

        bool isRunning = _player.Velocity.Length() >= MinimumAnimationSpeed;
        if (isRunning != _isRunning)
        {
            SetRunning(isRunning, restart: true);
        }

        if (!_isRunning)
        {
            _characterSprite.Frame = IdleFrame;
            UpdateDynamicShadowOccluder();
            return;
        }

        float framesPerSecond = GetMovementFramesPerSecond(_player.CurrentMovementMode);
        _frameCursor = Mathf.PosMod(
            _frameCursor + framesPerSecond * deltaSeconds,
            RunFrameCount);
        _characterSprite.Frame = Math.Clamp(
            (int)Mathf.Floor(_frameCursor),
            0,
            RunFrameCount - 1);
        UpdateDynamicShadowOccluder();
    }

    private void SetRunning(bool isRunning, bool restart)
    {
        _isRunning = isRunning;
        if (restart)
        {
            _frameCursor = 0.0f;
        }

        _characterSprite.Frame = isRunning ? 0 : IdleFrame;
    }

    private void ApplyFacingSide(FacingSide side)
    {
        _facingSide = side;

        bool isLeftSide = side == FacingSide.Left;

        // The authored body faces right. Crossing the character on the X axis flips
        // the same animation immediately; vertical aim does not delay the side change.
        _characterSprite.FlipH = isLeftSide;

        // AimPivot owns rotation. A negative local Y scale on the left side combines
        // with the pivot's ~180-degree rotation into the required horizontal mirror.
        // Unlike Sprite2D.FlipV, the transform is also inherited by MuzzlePoint.
        _weaponSprite.FlipH = false;
        _weaponSprite.FlipV = false;
        _weaponSprite.Scale = new Vector2(
            _weaponBaseScale.X,
            isLeftSide ? -_weaponBaseScale.Y : _weaponBaseScale.Y);

        // Player depth is changed continuously by DepthSortAnchor2D. Therefore the
        // weapon must stay relative to Player as well. CharacterVisual and AimPivot
        // are siblings, so convert the character's local Z into AimPivot-local Z and
        // place the weapon exactly one layer behind/on top of the body.
        int characterZRelativeToAimPivot = ZIndex - _aimPivot.ZIndex;
        _weaponSprite.ZIndex = characterZRelativeToAimPivot + (isLeftSide ? -1 : 1);
    }

    private void BuildDynamicShadowPolygons()
    {
        Texture2D texture = CharacterTexture
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} requires {nameof(CharacterTexture)}.");
        Image image = texture.GetImage();
        if (image.IsCompressed())
        {
            Error error = image.Decompress();
            if (error != Error.Ok)
            {
                throw new InvalidOperationException(
                    $"{nameof(PlayerMiniDayzPresentation2D)} could not decompress the character texture for dynamic shadows.");
            }
        }

        _shadowPolygons = new Vector2[RunFrameCount][];
        for (int frame = 0; frame < RunFrameCount; frame++)
        {
            _shadowPolygons[frame] = BuildFrameShadowPolygon(image, frame);
        }
    }

    private void ConfigureMuzzleSelfShadowOccluder()
    {
        OccluderPolygon2D polygon = _muzzleSelfShadowOccluder.Occluder ?? new OccluderPolygon2D();
        polygon.Closed = true;
        _muzzleSelfShadowOccluder.Occluder = polygon;
        _muzzleSelfShadowOccluder.Visible = true;
    }

    private void UpdateDynamicShadowOccluder(bool force = false)
    {
        if (_shadowPolygons.Length == 0)
        {
            return;
        }

        int frame = Math.Clamp(_characterSprite.Frame, 0, _shadowPolygons.Length - 1);
        if (!force && _lastShadowFrame == frame && _lastShadowFacingSide == _facingSide)
        {
            return;
        }

        Vector2[] polygon = _shadowPolygons[frame];
        if (_facingSide == FacingSide.Left)
        {
            polygon = MirrorPolygonHorizontally(polygon);
        }

        OccluderPolygon2D occluder = _muzzleSelfShadowOccluder.Occluder
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} requires a configured muzzle-shadow occluder.");
        occluder.Polygon = polygon;

        _lastShadowFrame = frame;
        _lastShadowFacingSide = _facingSide;
    }

    private static Vector2[] BuildFrameShadowPolygon(Image image, int frame)
    {
        int frameStartX = frame * SourceFrameWidth;
        List<Vector2> leftSide = new(SourceFrameHeight);
        List<Vector2> rightSide = new(SourceFrameHeight);

        for (int y = 0; y < SourceFrameHeight; y += MaximumShadowRowStep)
        {
            if (!TryFindOpaqueSpan(image, frameStartX, y, out int minX, out int maxX))
            {
                continue;
            }

            float localY = y - SourceFrameHeight * 0.5f + 0.5f;
            leftSide.Add(new Vector2(minX - SourceFrameWidth * 0.5f + 0.5f, localY));
            rightSide.Add(new Vector2(maxX - SourceFrameWidth * 0.5f + 0.5f, localY));
        }

        if (leftSide.Count < 2 || rightSide.Count < 2)
        {
            return CreateFallbackShadowPolygon();
        }

        List<Vector2> polygon = new(leftSide.Count + rightSide.Count + 4);
        AppendUnique(polygon, leftSide[0] + Vector2.Up * 0.75f);
        foreach (Vector2 point in leftSide)
        {
            AppendUnique(polygon, point);
        }

        for (int index = rightSide.Count - 1; index >= 0; index--)
        {
            AppendUnique(polygon, rightSide[index]);
        }

        AppendUnique(polygon, rightSide[0] + Vector2.Up * 0.75f);

        return polygon.ToArray();
    }

    private static bool TryFindOpaqueSpan(
        Image image,
        int frameStartX,
        int y,
        out int minX,
        out int maxX)
    {
        minX = int.MaxValue;
        maxX = int.MinValue;

        for (int x = 0; x < SourceFrameWidth; x++)
        {
            Color pixel = image.GetPixel(frameStartX + x, y);
            if (pixel.A < ShadowAlphaThreshold)
            {
                continue;
            }

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
        }

        return minX <= maxX;
    }

    private static Vector2[] MirrorPolygonHorizontally(Vector2[] polygon)
    {
        Vector2[] mirrored = new Vector2[polygon.Length];
        for (int index = 0; index < polygon.Length; index++)
        {
            Vector2 point = polygon[index];
            mirrored[index] = new Vector2(-point.X, point.Y);
        }

        return mirrored;
    }

    private static Vector2[] CreateFallbackShadowPolygon()
    {
        return
        [
            new Vector2(-7.5f, -13.5f),
            new Vector2(5.5f, -13.5f),
            new Vector2(9.5f, -8.5f),
            new Vector2(9.5f, 5.5f),
            new Vector2(5.5f, 10.5f),
            new Vector2(-6.5f, 10.5f),
            new Vector2(-9.5f, 5.5f),
            new Vector2(-9.5f, -8.5f)
        ];
    }

    private static void AppendUnique(List<Vector2> points, Vector2 point)
    {
        if (points.Count > 0)
        {
            Vector2 last = points[^1];
            if (last.IsEqualApprox(point))
            {
                return;
            }
        }

        points.Add(point);
    }

    private FacingSide ResolveAimSide()
    {
        float aimDirectionX = Mathf.Cos(_aimPivot.GlobalRotation);
        return aimDirectionX < 0.0f ? FacingSide.Left : FacingSide.Right;
    }

    private float GetMovementFramesPerSecond(MovementMode movementMode)
    {
        return movementMode switch
        {
            MovementMode.Sprint => SprintFramesPerSecond,
            MovementMode.Walk => WalkFramesPerSecond,
            _ => throw new InvalidOperationException("Unknown player movement mode.")
        };
    }

    private static void ValidateTexture(
        Texture2D? texture,
        string propertyName,
        int expectedFrames)
    {
        if (texture is null)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} requires {propertyName}.");
        }

        int expectedWidth = SourceFrameWidth * expectedFrames;
        if (texture.GetWidth() != expectedWidth || texture.GetHeight() != SourceFrameHeight)
        {
            throw new InvalidOperationException(
                $"{propertyName} must be {expectedWidth}x{SourceFrameHeight}, " +
                $"but is {texture.GetWidth()}x{texture.GetHeight()}.");
        }
    }

    private static void ValidateFramesPerSecond(float value, string propertyName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} requires positive finite {propertyName}.");
        }
    }

    private TNode RequireNode<TNode>(string path) where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} on '{Name}' requires '{path}'.");
    }
}
