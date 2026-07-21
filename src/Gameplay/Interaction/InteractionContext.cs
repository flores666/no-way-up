using System;
using Godot;

namespace LineZero.Gameplay.Interaction;

public sealed class InteractionContext
{
    public InteractionContext(Node actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Actor = actor;
    }

    public Node Actor { get; }
}

