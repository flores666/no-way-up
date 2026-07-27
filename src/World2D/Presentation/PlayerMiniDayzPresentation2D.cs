using System;
using Godot;
using LineZero.Gameplay.Movement;

namespace LineZero.World2D.Presentation;

public sealed partial class PlayerMiniDayzPresentation2D : Node2D
{
    private const float MinimumFacingHorizontal = 3.0f;
    private const float MinimumAnimationSpeed = 4.0f;

    private PlayerController2D _player = null!;
    private Node2D _bodyRig = null!;
    private Node2D _aimPivot = null!;
    private Polygon2D _frontLeg = null!;
    private Polygon2D _rearLeg = null!;
    private Polygon2D _groundShadow = null!;
    private float _stridePhase;
    private float _facingSign = 1.0f;

    public override void _Ready()
    {
        _player = GetParent() as PlayerController2D
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} on '{Name}' requires " +
                $"a {nameof(PlayerController2D)} parent.");
        _bodyRig = RequireNode<Node2D>("%BodyRig");
        _aimPivot = RequireNode<Node2D>("%AimPivot");
        _frontLeg = RequireNode<Polygon2D>("%FrontLeg");
        _rearLeg = RequireNode<Polygon2D>("%RearLeg");
        _groundShadow = RequireNode<Polygon2D>("%GroundShadow");
    }

    public override void _Process(double delta)
    {
        Vector2 aimDirection = GetGlobalMousePosition() - _player.GlobalPosition;
        if (Mathf.Abs(aimDirection.X) >= MinimumFacingHorizontal)
        {
            _facingSign = aimDirection.X < 0.0f ? -1.0f : 1.0f;
        }

        Scale = new Vector2(_facingSign, 1.0f);
        UpdateStride((float)delta);
        UpdatePosture();
        UpdateWeaponLayering(aimDirection);
    }

    private void UpdateStride(float delta)
    {
        float speed = _player.Velocity.Length();
        if (speed >= MinimumAnimationSpeed)
        {
            float cadence = _player.CurrentMovementMode == MovementMode.Sprint
                ? 13.0f
                : 8.5f;
            _stridePhase = Mathf.PosMod(_stridePhase + cadence * delta, Mathf.Tau);
            return;
        }

        _stridePhase = Mathf.Lerp(_stridePhase, 0.0f, Mathf.Clamp(delta * 8.0f, 0.0f, 1.0f));
    }

    private void UpdatePosture()
    {
        float speedFactor = Mathf.Clamp(_player.Velocity.Length() / 230.0f, 0.0f, 1.0f);
        float stride = Mathf.Sin(_stridePhase);
        float bob = Mathf.Abs(Mathf.Sin(_stridePhase * 2.0f));

        Vector2 rigPosition;
        Vector2 rigScale;
        float rigRotation;
        float legSwing;
        float aimHeight;
        Vector2 shadowScale;

        switch (_player.CurrentMovementMode)
        {
            case MovementMode.Crouch:
                rigPosition = new Vector2(0.0f, 8.0f + bob * 0.7f);
                rigScale = new Vector2(1.0f, 0.82f);
                rigRotation = -0.04f;
                legSwing = stride * 0.10f * speedFactor;
                aimHeight = -20.0f;
                shadowScale = new Vector2(1.12f, 0.88f);
                break;
            case MovementMode.Crawl:
                rigPosition = new Vector2(1.0f, 12.0f);
                rigScale = new Vector2(0.92f, 0.78f);
                rigRotation = 1.22f;
                legSwing = stride * 0.07f * speedFactor;
                aimHeight = -5.0f;
                shadowScale = new Vector2(1.55f, 0.78f);
                break;
            case MovementMode.Sprint:
                rigPosition = new Vector2(0.0f, -1.0f + bob * 1.8f);
                rigScale = new Vector2(1.03f, 1.03f);
                rigRotation = -0.08f;
                legSwing = stride * 0.36f * speedFactor;
                aimHeight = -27.0f;
                shadowScale = new Vector2(1.15f, 0.92f);
                break;
            default:
                rigPosition = new Vector2(0.0f, bob * 1.0f);
                rigScale = Vector2.One;
                rigRotation = -0.025f;
                legSwing = stride * 0.24f * speedFactor;
                aimHeight = -28.0f;
                shadowScale = Vector2.One;
                break;
        }

        _bodyRig.Position = rigPosition;
        _bodyRig.Scale = rigScale;
        _bodyRig.Rotation = rigRotation;
        _frontLeg.Rotation = legSwing;
        _rearLeg.Rotation = -legSwing;
        _groundShadow.Scale = shadowScale;
        _aimPivot.Position = new Vector2(0.0f, aimHeight + bob * 0.35f);
    }

    private void UpdateWeaponLayering(Vector2 aimDirection)
    {
        if (aimDirection.LengthSquared() <= 0.0001f)
        {
            return;
        }

        bool pointsLeft = aimDirection.X < 0.0f;
        _aimPivot.Scale = new Vector2(1.0f, pointsLeft ? -1.0f : 1.0f);
        _aimPivot.ZIndex = aimDirection.Y < -4.0f ? 3 : 12;
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerMiniDayzPresentation2D)} on '{Name}' requires '{path}'.");
    }
}
