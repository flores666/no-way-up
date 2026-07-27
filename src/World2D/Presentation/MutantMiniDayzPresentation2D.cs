using System;
using Godot;
using LineZero.Gameplay.Enemies;
using LineZero.World2D.Enemies;

namespace LineZero.World2D.Presentation;

public sealed partial class MutantMiniDayzPresentation2D : Node
{
    private MutantController2D _mutant = null!;
    private Node2D _bodyRig = null!;
    private Polygon2D _frontLeg = null!;
    private Polygon2D _rearLeg = null!;
    private Polygon2D _shadow = null!;
    private float _stridePhase;

    public override void _Ready()
    {
        _mutant = GetParent() as MutantController2D
            ?? throw new InvalidOperationException(
                $"{nameof(MutantMiniDayzPresentation2D)} on '{Name}' requires " +
                $"a {nameof(MutantController2D)} parent.");
        _bodyRig = RequireNode<Node2D>("%MutantBodyRig");
        _frontLeg = RequireNode<Polygon2D>("%MutantFrontLeg");
        _rearLeg = RequireNode<Polygon2D>("%MutantRearLeg");
        _shadow = RequireNode<Polygon2D>("%MutantGroundShadow");
    }

    public override void _Process(double delta)
    {
        if (_mutant.State == MutantState.Dead)
        {
            ApplyDeadPose();
            return;
        }

        float speed = _mutant.Velocity.Length();
        if (speed >= 3.0f)
        {
            _stridePhase = Mathf.PosMod(
                _stridePhase + (7.0f + speed / 55.0f) * (float)delta,
                Mathf.Tau);
        }
        else
        {
            _stridePhase = Mathf.Lerp(
                _stridePhase,
                0.0f,
                Mathf.Clamp((float)delta * 7.0f, 0.0f, 1.0f));
        }

        float stride = Mathf.Sin(_stridePhase) * Mathf.Clamp(speed / 95.0f, 0.0f, 1.0f);
        float bob = Mathf.Abs(Mathf.Sin(_stridePhase * 2.0f));
        _bodyRig.Position = new Vector2(0.0f, bob * 1.2f);
        _bodyRig.Rotation = -0.07f;
        _bodyRig.Scale = Vector2.One;
        _frontLeg.Rotation = stride * 0.28f;
        _rearLeg.Rotation = -stride * 0.28f;
        _shadow.Scale = Vector2.One;
    }

    private void ApplyDeadPose()
    {
        _bodyRig.Position = new Vector2(2.0f, 12.0f);
        _bodyRig.Rotation = 1.28f;
        _bodyRig.Scale = new Vector2(0.92f, 0.92f);
        _frontLeg.Rotation = 0.18f;
        _rearLeg.Rotation = -0.24f;
        _shadow.Scale = new Vector2(1.55f, 0.78f);
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(MutantMiniDayzPresentation2D)} on '{Name}' requires '{path}'.");
    }
}
