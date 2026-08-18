using System;
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

    private enum FacingSide
    {
        Left,
        Right
    }

    private PlayerController2D _player = null!;
    private Node2D _aimPivot = null!;
    private Sprite2D _characterSprite = null!;
    private Sprite2D _weaponSprite = null!;
    private bool _isRunning;
    private FacingSide _facingSide = FacingSide.Right;
    private float _frameCursor;
    private Vector2 _weaponBaseScale;

    [Export]
    public Texture2D? CharacterTexture { get; set; }

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float WalkFramesPerSecond { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float SprintFramesPerSecond { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float CrouchFramesPerSecond { get; set; } = 7.0f;

    public override void _Ready()
    {
        _player = GetParent() as PlayerController2D
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} on '{Name}' requires a {nameof(PlayerController2D)} parent.");
        _aimPivot = RequireNode<Node2D>("%AimPivot");
        _characterSprite = RequireNode<Sprite2D>("%CharacterSprite");
        _weaponSprite = RequireNode<Sprite2D>("%WeaponSprite");

        ValidateTexture(CharacterTexture, nameof(CharacterTexture), RunFrameCount);
        ValidateTexture(_weaponSprite.Texture, "WeaponSprite.Texture", expectedFrames: 1);
        ValidateFramesPerSecond(WalkFramesPerSecond, nameof(WalkFramesPerSecond));
        ValidateFramesPerSecond(SprintFramesPerSecond, nameof(SprintFramesPerSecond));
        ValidateFramesPerSecond(CrouchFramesPerSecond, nameof(CrouchFramesPerSecond));

        _characterSprite.Texture = CharacterTexture;
        _characterSprite.Hframes = RunFrameCount;
        _characterSprite.Vframes = 1;
        _weaponBaseScale = new Vector2(
            Mathf.Abs(_weaponSprite.Scale.X),
            Mathf.Abs(_weaponSprite.Scale.Y));
        _weaponSprite.ZAsRelative = true;
        _weaponSprite.FlipH = false;
        _weaponSprite.FlipV = false;

        SetRunning(isRunning: false, restart: true);
        ApplyFacingSide(ResolveAimSide());
    }

    public override void _Process(double delta)
    {
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
    }

    public override void _PhysicsProcess(double delta)
    {
        FacingSide facingSide = ResolveAimSide();
        if (facingSide != _facingSide)
        {
            ApplyFacingSide(facingSide);
        }
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
            MovementMode.Crouch => CrouchFramesPerSecond,
            MovementMode.Crawl => CrouchFramesPerSecond,
            _ => WalkFramesPerSecond
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
