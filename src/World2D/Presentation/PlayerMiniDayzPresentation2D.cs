using System;
using Godot;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Movement;
using LineZero.World2D.Combat;

namespace LineZero.World2D.Presentation;

public sealed partial class PlayerMiniDayzPresentation2D : Node2D
{
    private const int SourceFrameWidth = 64;
    private const int SourceFrameHeight = 64;
    private const float MinimumAnimationSpeed = 4.0f;
    private const float MinimumFacingDistanceSquared = 36.0f;
    private const string AimAction = "aim";

    private enum FacingDirection
    {
        Down,
        Up,
        Left,
        Right
    }

    private enum AnimationState
    {
        Idle,
        Run,
        Aim
    }

    private PlayerController2D _player = null!;
    private PlayerWeaponController2D _weaponController = null!;
    private Sprite2D _characterSprite = null!;
    private FacingDirection _facingDirection = FacingDirection.Down;
    private AnimationState _animationState = AnimationState.Idle;
    private float _frameCursor;
    private float _aimPoseRemainingSeconds;

    [Export] public Texture2D? IdleDownTexture { get; set; }
    [Export] public Texture2D? IdleUpTexture { get; set; }
    [Export] public Texture2D? IdleLeftTexture { get; set; }
    [Export] public Texture2D? IdleRightTexture { get; set; }
    [Export] public Texture2D? RunDownTexture { get; set; }
    [Export] public Texture2D? RunUpTexture { get; set; }
    [Export] public Texture2D? RunLeftTexture { get; set; }
    [Export] public Texture2D? RunRightTexture { get; set; }
    [Export] public Texture2D? AimDownTexture { get; set; }
    [Export] public Texture2D? AimUpTexture { get; set; }
    [Export] public Texture2D? AimLeftTexture { get; set; }
    [Export] public Texture2D? AimRightTexture { get; set; }

    [Export(PropertyHint.Range, "1,16,0.5")]
    public float IdleFramesPerSecond { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float WalkFramesPerSecond { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float SprintFramesPerSecond { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float CrouchFramesPerSecond { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "1,24,0.5")]
    public float AimFramesPerSecond { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "0.05,0.5,0.01")]
    public float AimPoseHoldSeconds { get; set; } = 0.18f;

    public override void _Ready()
    {
        _player = GetParent() as PlayerController2D
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} on '{Name}' requires a {nameof(PlayerController2D)} parent.");
        _characterSprite = RequireNode<Sprite2D>("%CharacterSprite");
        _weaponController = RequireNode<PlayerWeaponController2D>("%PlayerWeaponController2D");

        ValidateTexture(IdleDownTexture, nameof(IdleDownTexture), 9);
        ValidateTexture(IdleUpTexture, nameof(IdleUpTexture), 9);
        ValidateTexture(IdleLeftTexture, nameof(IdleLeftTexture), 9);
        ValidateTexture(IdleRightTexture, nameof(IdleRightTexture), 9);
        ValidateTexture(RunDownTexture, nameof(RunDownTexture), 6);
        ValidateTexture(RunUpTexture, nameof(RunUpTexture), 6);
        ValidateTexture(RunLeftTexture, nameof(RunLeftTexture), 6);
        ValidateTexture(RunRightTexture, nameof(RunRightTexture), 6);
        ValidateTexture(AimDownTexture, nameof(AimDownTexture), 1);
        ValidateTexture(AimUpTexture, nameof(AimUpTexture), 1);
        ValidateTexture(AimLeftTexture, nameof(AimLeftTexture), 1);
        ValidateTexture(AimRightTexture, nameof(AimRightTexture), 1);

        if (!float.IsFinite(AimPoseHoldSeconds) || AimPoseHoldSeconds <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} requires a positive finite aim-pose duration.");
        }

        _weaponController.ShotAttempted += OnShotAttempted;
        ApplyAnimation(AnimationState.Idle, FacingDirection.Down, restart: true);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_weaponController))
        {
            _weaponController.ShotAttempted -= OnShotAttempted;
        }
    }

    public override void _Process(double delta)
    {
        float deltaSeconds = double.IsFinite(delta) && delta > 0.0
            ? (float)delta
            : 0.0f;

        if (_aimPoseRemainingSeconds > 0.0f)
        {
            _aimPoseRemainingSeconds = Math.Max(0.0f, _aimPoseRemainingSeconds - deltaSeconds);
        }

        Vector2 velocity = _player.Velocity;
        bool isMoving = velocity.Length() >= MinimumAnimationSpeed;

        Vector2 aimDirection = GetGlobalMousePosition() - _player.GlobalPosition;
        FacingDirection direction = isMoving
            ? ResolveDirection(velocity)
            : aimDirection.LengthSquared() >= MinimumFacingDistanceSquared
                ? ResolveDirection(aimDirection)
                : _facingDirection;

        bool isAiming = _weaponController.IsCombatInputEnabled && Input.IsActionPressed(AimAction);
        AnimationState state = isAiming || _aimPoseRemainingSeconds > 0.0f
            ? AnimationState.Aim
            : isMoving ? AnimationState.Run : AnimationState.Idle;

        bool animationChanged = state != _animationState || direction != _facingDirection;
        if (animationChanged)
        {
            bool restart = state != _animationState;
            ApplyAnimation(state, direction, restart);
        }

        float framesPerSecond = state switch
        {
            AnimationState.Aim => AimFramesPerSecond,
            AnimationState.Run => GetMovementFramesPerSecond(_player.CurrentMovementMode),
            _ => IdleFramesPerSecond
        };

        int frameCount = Math.Max(_characterSprite.Hframes, 1);
        _frameCursor = Mathf.PosMod(_frameCursor + framesPerSecond * deltaSeconds, frameCount);
        _characterSprite.Frame = Math.Clamp((int)Mathf.Floor(_frameCursor), 0, frameCount - 1);
    }

    private void OnShotAttempted(FirearmShotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status is FirearmShotStatus.CombatDisabled or FirearmShotStatus.OwnerDead)
        {
            return;
        }

        _aimPoseRemainingSeconds = AimPoseHoldSeconds;
    }

    private void ApplyAnimation(
        AnimationState state,
        FacingDirection direction,
        bool restart)
    {
        Texture2D texture = ResolveTexture(state, direction);
        int frameCount = texture.GetWidth() / SourceFrameWidth;

        _animationState = state;
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

    private Texture2D ResolveTexture(AnimationState state, FacingDirection direction)
    {
        return (state, direction) switch
        {
            (AnimationState.Idle, FacingDirection.Down) => IdleDownTexture!,
            (AnimationState.Idle, FacingDirection.Up) => IdleUpTexture!,
            (AnimationState.Idle, FacingDirection.Left) => IdleLeftTexture!,
            (AnimationState.Idle, FacingDirection.Right) => IdleRightTexture!,
            (AnimationState.Run, FacingDirection.Down) => RunDownTexture!,
            (AnimationState.Run, FacingDirection.Up) => RunUpTexture!,
            (AnimationState.Run, FacingDirection.Left) => RunLeftTexture!,
            (AnimationState.Run, FacingDirection.Right) => RunRightTexture!,
            (AnimationState.Aim, FacingDirection.Down) => AimDownTexture!,
            (AnimationState.Aim, FacingDirection.Up) => AimUpTexture!,
            (AnimationState.Aim, FacingDirection.Left) => AimLeftTexture!,
            (AnimationState.Aim, FacingDirection.Right) => AimRightTexture!,
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

    private TNode RequireNode<TNode>(string path) where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} on '{Name}' requires '{path}'.");
    }
}
