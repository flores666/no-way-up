namespace LineZero.Gameplay.Interaction;

public interface IInteractable
{
    string InteractionPrompt { get; }

    bool CanInteract(InteractionContext context);

    InteractionResult Interact(InteractionContext context);
}

