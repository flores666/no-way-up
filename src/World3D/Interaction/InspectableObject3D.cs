using Godot;
using LineZero.Gameplay.Interaction;

namespace LineZero.World3D.Interaction;

public sealed partial class InspectableObject3D : Interactable3D
{
    private bool _hasBeenUsed;

    [Export]
    public string PromptText { get; set; } = "Inspect terminal";

    [Export(PropertyHint.MultilineText)]
    public string MessageText { get; set; } = "The terminal has no power.";

    [Export]
    public bool IsReusable { get; set; } = true;

    public override string InteractionPrompt => PromptText;

    public override bool CanInteract(InteractionContext context)
    {
        return IsReusable || !_hasBeenUsed;
    }

    public override InteractionResult Interact(InteractionContext context)
    {
        if (!CanInteract(context))
        {
            return InteractionResult.None;
        }

        _hasBeenUsed = true;
        return InteractionResult.WithMessage(MessageText);
    }
}
