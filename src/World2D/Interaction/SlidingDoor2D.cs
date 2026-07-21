using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Noise;
using LineZero.Gameplay.Power;
using LineZero.World2D.Noise;

namespace LineZero.World2D.Interaction;

public sealed partial class SlidingDoor2D : Interactable2D, INoiseEmitter2D
{
    private enum DoorState
    {
        Closed,
        Opening,
        Open,
    }

    private AnimatableBody2D _doorPanel = null!;
    private CollisionShape2D _doorCollision = null!;
    private CollisionShape2D _interactionShape = null!;
    private Vector2 _normalizedOpeningDirection;
    private Tween? _activeTween;
    private NoiseSystem2D? _noiseSystem;
    private PowerCircuitModel? _powerCircuit;
    private DoorState _state = DoorState.Closed;
    private bool _openedEventEmitted;
    private Vector2 _openingTargetPosition;

    [Export]
    public Vector2 OpeningDirection { get; set; } = Vector2.Up;

    [Export(PropertyHint.Range, "1.0,500.0,1.0,or_greater")]
    public float OpeningDistance { get; set; } = 190.0f;

    [Export(PropertyHint.Range, "0.05,5.0,0.05,or_greater")]
    public double AnimationDuration { get; set; } = 0.7;

    [Export]
    public bool RequiresPower { get; set; }

    [Export]
    public string PoweredInteractionPrompt { get; set; } = "Open emergency exit";

    [Export]
    public string UnpoweredInteractionPrompt { get; set; } =
        "Emergency exit — no power";

    [Export]
    public string NoPowerMessage { get; set; } =
        "The emergency exit has no power.";

    public override string InteractionPrompt
    {
        get
        {
            if (!RequiresPower)
            {
                return "Open door";
            }

            return _powerCircuit?.IsPowered == true
                ? PoweredInteractionPrompt
                : UnpoweredInteractionPrompt;
        }
    }

    public bool IsOpen => _state == DoorState.Open;

    public bool IsOpening => _state == DoorState.Opening;

    public event Action? OpeningStarted;

    public event Action<SlidingDoor2D>? Opened;

    public override void _Ready()
    {
        base._Ready();

        if (OpeningDirection.IsZeroApprox())
        {
            throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' requires an opening direction.");
        }

        if (OpeningDistance <= 0.0f || AnimationDuration <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' has invalid animation settings.");
        }

        if (RequiresPower &&
            (string.IsNullOrWhiteSpace(PoweredInteractionPrompt) ||
             string.IsNullOrWhiteSpace(UnpoweredInteractionPrompt) ||
             string.IsNullOrWhiteSpace(NoPowerMessage)))
        {
            throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' requires non-empty powered-door text.");
        }

        _normalizedOpeningDirection = OpeningDirection.Normalized();
        _doorPanel = GetNodeOrNull<AnimatableBody2D>("%DoorPanel")
            ?? throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' requires a DoorPanel node.");

        _doorCollision = GetNodeOrNull<CollisionShape2D>("%DoorCollision")
            ?? throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' requires a DoorCollision node.");
        _interactionShape = GetNodeOrNull<CollisionShape2D>("%InteractionShape")
            ?? throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' requires an InteractionShape node.");

        if (_doorCollision.Shape is null || _doorCollision.Disabled)
        {
            throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' requires an enabled door collision.");
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
        if (_state != DoorState.Closed)
        {
            return false;
        }

        return context.Actor is not IHealthOwner healthOwner || healthOwner.Health.IsAlive;
    }

    public override InteractionResult Interact(InteractionContext context)
    {
        if (!CanInteract(context))
        {
            return InteractionResult.None;
        }

        if (RequiresPower)
        {
            PowerCircuitModel circuit = _powerCircuit
                ?? throw new InvalidOperationException(
                    $"{nameof(SlidingDoor2D)} on '{Name}' requires a bound power circuit.");
            if (!circuit.IsPowered)
            {
                return InteractionResult.WithMessage(NoPowerMessage);
            }
        }

        NoiseSystem2D noiseSystem = _noiseSystem
            ?? throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' has no bound noise system.");
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
            GlobalPosition,
            _doorPanel,
            RequiresPower ? "Emergency exit opening" : "Sliding door opening");
        SafeEventPublisher.Publish(OpeningStarted, nameof(OpeningStarted));
        return InteractionResult.None;
    }

    public void BindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' already has a noise system.");
        }

        if (!GodotObject.IsInstanceValid(noiseSystem) || !noiseSystem.IsInsideTree())
        {
            throw new ArgumentException("The noise system must be active.", nameof(noiseSystem));
        }

        _noiseSystem = noiseSystem;
    }

    public void UnbindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (ReferenceEquals(_noiseSystem, noiseSystem))
        {
            _noiseSystem = null;
        }
    }

    public void BindPowerCircuit(PowerCircuitModel circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (!RequiresPower)
        {
            throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' is not configured to require power.");
        }

        if (_powerCircuit is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(SlidingDoor2D)} on '{Name}' already has a power circuit.");
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

        // A naturally completed tween owns the final state. Snap to the authored
        // target to remove floating-point drift before opening the collision gate.
        _doorPanel.Position = _openingTargetPosition;
        _doorCollision.Disabled = true;
        if (!_doorCollision.Disabled)
        {
            _state = DoorState.Closed;
            return;
        }

        // An open door is terminal and no longer participates in interaction
        // selection. This is an invariant, not an input-side workaround.
        _interactionShape.Disabled = true;
        _state = DoorState.Open;

        if (_openedEventEmitted)
        {
            return;
        }

        _openedEventEmitted = true;
        SafeEventPublisher.Publish(Opened, this, nameof(Opened));
    }
}
