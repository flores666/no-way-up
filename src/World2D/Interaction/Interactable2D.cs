using System;
using Godot;
using LineZero.Gameplay.Interaction;

namespace LineZero.World2D.Interaction;

public abstract partial class Interactable2D : Area2D, IInteractable
{
    [Export(PropertyHint.Range, "-10,10,1")]
    public int InteractionPriority { get; set; }

    public abstract string InteractionPrompt { get; }

    public Vector2 InteractionPosition => GlobalPosition;

    public override void _Ready()
    {
        CollisionShape2D interactionShape = GetNodeOrNull<CollisionShape2D>(
            "%InteractionShape")
            ?? throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' requires an InteractionShape node.");

        if (interactionShape.Shape is null || interactionShape.Disabled)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' requires an enabled interaction shape.");
        }

        if (CollisionLayer != CollisionLayers2D.Interaction || CollisionMask != 0)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' must use only the Interaction layer.");
        }

        if (!Monitorable)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' must be monitorable.");
        }

        if (string.IsNullOrWhiteSpace(InteractionPrompt))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' requires a non-empty interaction prompt.");
        }
    }

    public abstract bool CanInteract(InteractionContext context);

    public abstract InteractionResult Interact(InteractionContext context);
}

