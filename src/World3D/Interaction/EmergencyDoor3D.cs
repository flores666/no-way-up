using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Noise;
using LineZero.Gameplay.Power;
using LineZero.World3D.Noise;

namespace LineZero.World3D.Interaction;

public sealed partial class EmergencyDoor3D : Interactable3D
{
    private enum DoorState
    {
        Closed,
        Opening,
        Open
    }

    private AnimatableBody3D _doorPanel = null!;
    private CollisionShape3D _doorCollision = null!;
    private CollisionShape3D _interactionShape = null!;
    private Vector3 _normalizedOpeningDirection;
    private Vector3 _openingTargetPosition;
    private Tween? _activeTween;
    private NoiseSystem3D? _noiseSystem;
    private PowerCircuitModel? _powerCircuit;
    private DoorState _state;
    private bool _openedEventPublished;

    [Export]
    public Vector3 OpeningDirection { get; set; } = Vector3.Right;

    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float OpeningDistance { get; set; } = 4.5f;

    [Export(PropertyHint.Range, "0.05,5.0,0.05")]
    public double AnimationDuration { get; set; } = 0.7;

    [Export]
    public string PoweredInteractionPrompt { get; set; } =
        "Open emergency exit";

    [Export]
    public string UnpoweredInteractionPrompt { get; set; } =
        "Emergency exit — no power";

    [Export]
    public string NoPowerMessage { get; set; } =
        "The emergency exit has no power.";

    public override string InteractionPrompt => _powerCircuit?.IsPowered == true
        ? PoweredInteractionPrompt
        : UnpoweredInteractionPrompt;

    public bool IsOpen => _state == DoorState.Open;

    public bool IsOpening => _state == DoorState.Opening;

    public override CollisionObject3D? InteractionOccluder => _doorPanel;

    public event Action? OpeningStarted;

    public event Action<EmergencyDoor3D>? Opened;

    public override void _Ready()
    {
        base._Ready();
        if (!OpeningDirection.IsFinite() || OpeningDirection.IsZeroApprox() ||
            !float.IsFinite(OpeningDistance) || OpeningDistance <= 0.0f ||
            !double.IsFinite(AnimationDuration) || AnimationDuration <= 0.0 ||
            string.IsNullOrWhiteSpace(PoweredInteractionPrompt) ||
            string.IsNullOrWhiteSpace(UnpoweredInteractionPrompt) ||
            string.IsNullOrWhiteSpace(NoPowerMessage))
        {
            throw new InvalidOperationException(
                $"{nameof(EmergencyDoor3D)} on '{Name}' has invalid configuration.");
        }

        _normalizedOpeningDirection = OpeningDirection.Normalized();
        _doorPanel = GetNodeOrNull<AnimatableBody3D>("%DoorPanel3D")
            ?? throw new InvalidOperationException(
                $"{nameof(EmergencyDoor3D)} on '{Name}' requires DoorPanel3D.");
        _doorCollision = GetNodeOrNull<CollisionShape3D>("%DoorCollision3D")
            ?? throw new InvalidOperationException(
                $"{nameof(EmergencyDoor3D)} on '{Name}' requires DoorCollision3D.");
        _interactionShape = GetNodeOrNull<CollisionShape3D>("%InteractionShape3D")
            ?? throw new InvalidOperationException(
                $"{nameof(EmergencyDoor3D)} on '{Name}' requires InteractionShape3D.");
        if (_doorPanel.CollisionLayer != CollisionLayers3D.World ||
            _doorCollision.Shape is null ||
            _doorCollision.Disabled)
        {
            throw new InvalidOperationException(
                $"{nameof(EmergencyDoor3D)} on '{Name}' requires an enabled world collision.");
        }
    }

    public override void _ExitTree()
    {
        if (_activeTween is not null && GodotObject.IsInstanceValid(_activeTween))
        {
            _activeTween.Finished -= OnOpeningFinished;
            _activeTween.Kill();
        }

        _activeTween = null;
        _noiseSystem = null;
        _powerCircuit = null;
    }

    public override bool CanInteract(InteractionContext context)
    {
        return _state == DoorState.Closed &&
               _noiseSystem is not null &&
               _powerCircuit is not null &&
               (context.Actor is not IHealthOwner healthOwner ||
                healthOwner.Health.IsAlive);
    }

    public override InteractionResult Interact(InteractionContext context)
    {
        if (!CanInteract(context))
        {
            return InteractionResult.None;
        }

        PowerCircuitModel circuit = _powerCircuit
            ?? throw new InvalidOperationException("The 3D emergency door has no circuit.");
        if (!circuit.IsPowered)
        {
            return InteractionResult.WithMessage(NoPowerMessage);
        }

        NoiseSystem3D noiseSystem = _noiseSystem
            ?? throw new InvalidOperationException("The 3D emergency door has no noise system.");
        _state = DoorState.Opening;
        _openingTargetPosition = _doorPanel.Position +
                                 (_normalizedOpeningDirection * OpeningDistance);
        _activeTween = CreateTween();
        _activeTween.SetTrans(Tween.TransitionType.Cubic);
        _activeTween.SetEase(Tween.EaseType.InOut);
        _activeTween.TweenProperty(
            _doorPanel,
            new NodePath("position"),
            _openingTargetPosition,
            AnimationDuration);
        _activeTween.Finished += OnOpeningFinished;

        noiseSystem.EmitNoise(
            context.Actor,
            NoiseKind.Interaction,
            1.0f,
            GlobalPosition + (Vector3.Up * 1.0f),
            _doorPanel,
            "Emergency exit opening");
        SafeEventPublisher.Publish(
            OpeningStarted,
            $"{nameof(EmergencyDoor3D)}.{nameof(OpeningStarted)}");
        return InteractionResult.None;
    }

    public void BindNoiseSystem(NoiseSystem3D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(EmergencyDoor3D)} on '{Name}' already has a noise system.");
        }

        if (!GodotObject.IsInstanceValid(noiseSystem) || !noiseSystem.IsInsideTree())
        {
            throw new ArgumentException("The noise system must be active.", nameof(noiseSystem));
        }

        _noiseSystem = noiseSystem;
    }

    public void BindPowerCircuit(PowerCircuitModel circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (_powerCircuit is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(EmergencyDoor3D)} on '{Name}' already has a power circuit.");
        }

        _powerCircuit = circuit;
    }

    private void OnOpeningFinished()
    {
        if (_activeTween is not null)
        {
            _activeTween.Finished -= OnOpeningFinished;
        }

        _activeTween = null;
        if (_state != DoorState.Opening)
        {
            return;
        }

        if (!IsInsideTree() ||
            !GodotObject.IsInstanceValid(_doorPanel) ||
            !GodotObject.IsInstanceValid(_doorCollision) ||
            !GodotObject.IsInstanceValid(_interactionShape))
        {
            _state = DoorState.Closed;
            return;
        }

        _doorPanel.Position = _openingTargetPosition;
        _doorCollision.Disabled = true;
        if (!_doorCollision.Disabled)
        {
            _state = DoorState.Closed;
            return;
        }

        _interactionShape.Disabled = true;
        _state = DoorState.Open;
        if (_openedEventPublished)
        {
            return;
        }

        _openedEventPublished = true;
        SafeEventPublisher.Publish(
            Opened,
            this,
            $"{nameof(EmergencyDoor3D)}.{nameof(Opened)}");
    }
}
