using System;
using Godot;
using LineZero.Gameplay.Interaction;

namespace LineZero.World3D.Interaction;

public abstract partial class Interactable3D : Area3D, IInteractable
{
    [Export(PropertyHint.Range, "-10,10,1")]
    public int InteractionPriority { get; set; }

    public abstract string InteractionPrompt { get; }

    public Vector3 InteractionPosition => GlobalPosition;

    public virtual CollisionObject3D? InteractionOccluder => null;

    public override void _Ready()
    {
        CollisionShape3D interactionShape =
            GetNodeOrNull<CollisionShape3D>("%InteractionShape3D")
            ?? throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' requires InteractionShape3D.");
        if (interactionShape.Shape is null || interactionShape.Disabled)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' requires an enabled interaction shape.");
        }

        if (CollisionLayer != CollisionLayers3D.InteractionArea ||
            CollisionMask != 0 ||
            !Monitorable)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' must use only the monitorable " +
                "3D interaction layer.");
        }

        if (string.IsNullOrWhiteSpace(InteractionPrompt))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' requires a non-empty prompt.");
        }
    }

    public abstract bool CanInteract(InteractionContext context);

    public abstract InteractionResult Interact(InteractionContext context);
}
