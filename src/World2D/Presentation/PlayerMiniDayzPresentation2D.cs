using System;
using Godot;
using LineZero.Gameplay.Movement;

namespace LineZero.World2D.Presentation;

public sealed partial class PlayerMiniDayzPresentation2D : Node2D
{
    private const int SourceFrameWidth = 88;
    private const int SourceFrameHeight = 88;
    private const float MinimumAnimationSpeed = 4.0f;
    private const float MinimumFacingDistanceSquared = 36.0f;

    private enum FacingDirection
    {
        Down,
        Up,
        Left,
        Right
    }

    private PlayerController2D _player = null!;
    private Sprite2D _characterSprite = null!;
    private FacingDirection _facingDirection = FacingDirection.Down;
    private bool _isMoving;
    private float _frameCursor;

    [Export] public Texture2D? IdleDownTexture { get; set; }
    [Export] public Texture2D? IdleUpTexture { get; set; }
    [Export] public Texture2D? IdleLeftTexture { get; set; }
    [Export] public Texture2D? IdleRightTexture { get; set; }
    [Export] public Texture2D? RunDownTexture { get; set; }
    [Export] public Texture2D? RunUpTexture { get; set; }
    [Export] public Texture2D? RunLeftTexture { get; set; }
    [Export] public Texture2D? RunRightTexture { get; set; }

    [Export(PropertyHint.Range, "1,16,0.5")]
    public float IdleFramesPerSecond { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float WalkFramesPerSecond { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float SprintFramesPerSecond { get; set; } = 11.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float CrouchFramesPerSecond { get; set; } = 5.0f;

    public override void _Ready()
    {
        _player = GetParent() as PlayerController2D
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} on '{Name}' requires a {nameof(PlayerController2D)} parent.");
        _characterSprite = RequireNode<Sprite2D>("%CharacterSprite");

        ValidateTexture(IdleDownTexture, nameof(IdleDownTexture), 5);
        ValidateTexture(IdleUpTexture, nameof(IdleUpTexture), 5);
        ValidateTexture(IdleLeftTexture, nameof(IdleLeftTexture), 5);
        ValidateTexture(IdleRightTexture, nameof(IdleRightTexture), 5);
        ValidateTexture(RunDownTexture, nameof(RunDownTexture), 5);
        ValidateTexture(RunUpTexture, nameof(RunUpTexture), 5);
        ValidateTexture(RunLeftTexture, nameof(RunLeftTexture), 5);
        ValidateTexture(RunRightTexture, nameof(RunRightTexture), 5);

        ApplyAnimation(false, FacingDirection.Down, true);
    }

    public override void _Process(double delta)
    {
        Vector2 velocity = _player.Velocity;
        float speed = velocity.Length();
        bool isMoving = speed >= MinimumAnimationSpeed;

        Vector2 aimDirection = GetGlobalMousePosition() - _player.GlobalPosition;
        FacingDirection direction = aimDirection.LengthSquared() >= MinimumFacingDistanceSquared
            ? ResolveDirection(aimDirection)
            : isMoving ? ResolveDirection(velocity) : _facingDirection;

        bool animationChanged = isMoving != _isMoving || direction != _facingDirection;
        if (animationChanged)
        {
            ApplyAnimation(isMoving, direction, isMoving != _isMoving);
        }

        float framesPerSecond = isMoving
            ? GetMovementFramesPerSecond(_player.CurrentMovementMode)
            : IdleFramesPerSecond;

        int frameCount = Math.Max(_characterSprite.Hframes, 1);
        _frameCursor = Mathf.PosMod(_frameCursor + framesPerSecond * (float)delta, frameCount);
        _characterSprite.Frame = Math.Clamp((int)Mathf.Floor(_frameCursor), 0, frameCount - 1);
    }

    private void ApplyAnimation(bool isMoving, FacingDirection direction, bool restart)
    {
        Texture2D texture = ResolveTexture(isMoving, direction);
        int frameCount = texture.GetWidth() / SourceFrameWidth;

        _isMoving = isMoving;
        _facingDirection = direction;
        _characterSprite.Texture = texture;
        _characterSprite.Hframes = frameCount;
        _characterSprite.Vframes = 1;

        if (restart)
        {
            _frameCursor = 0.0f;
        }
        else
        {
            _frameCursor = Mathf.PosMod(_frameCursor, frameCount);
        }

        _characterSprite.Frame = Math.Clamp((int)Mathf.Floor(_frameCursor), 0, frameCount - 1);
    }

    private Texture2D ResolveTexture(bool isMoving, FacingDirection direction)
    {
        return (isMoving, direction) switch
        {
            (false, FacingDirection.Down) => IdleDownTexture!,
            (false, FacingDirection.Up) => IdleUpTexture!,
            (false, FacingDirection.Left) => IdleLeftTexture!,
            (false, FacingDirection.Right) => IdleRightTexture!,
            (true, FacingDirection.Down) => RunDownTexture!,
            (true, FacingDirection.Up) => RunUpTexture!,
            (true, FacingDirection.Left) => RunLeftTexture!,
            (true, FacingDirection.Right) => RunRightTexture!,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
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

    private static FacingDirection ResolveDirection(Vector2 direction)
    {
        if (Mathf.Abs(direction.X) > Mathf.Abs(direction.Y))
        {
            return direction.X < 0.0f ? FacingDirection.Left : FacingDirection.Right;
        }
        return direction.Y < 0.0f ? FacingDirection.Up : FacingDirection.Down;
    }

    private static void ValidateTexture(Texture2D? texture, string propertyName, int expectedFrames)
    {
        if (texture is null)
        {
            throw new InvalidOperationException($"{nameof(PlayerMiniDayzPresentation2D)} requires {propertyName}.");
        }
        int expectedWidth = SourceFrameWidth * expectedFrames;
        if (texture.GetWidth() != expectedWidth || texture.GetHeight() != SourceFrameHeight)
        {
            throw new InvalidOperationException(
                $"{propertyName} must be {expectedWidth}x{SourceFrameHeight}, but is {texture.GetWidth()}x{texture.GetHeight()}.");
        }
    }

    private TNode RequireNode<TNode>(string path) where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException($"{nameof(PlayerMiniDayzPresentation2D)} on '{Name}' requires '{path}'.");
    }
}
