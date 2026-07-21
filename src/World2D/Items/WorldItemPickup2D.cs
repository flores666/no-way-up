using System;
using Godot;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;
using LineZero.World2D.Interaction;

namespace LineZero.World2D.Items;

public sealed partial class WorldItemPickup2D : Interactable2D
{
    private CollisionShape2D _interactionShape = null!;
    private Node2D _visualRoot = null!;
    private Polygon2D _bodyVisual = null!;
    private Polygon2D _coreVisual = null!;
    private Label _pickupLabel = null!;
    private bool _isAvailable = true;
    private bool _isRemovalQueued;

    [Export]
    public ItemDefinition? ItemDefinition { get; set; }

    [Export(PropertyHint.Range, "1,9999,1,or_greater")]
    public int Quantity { get; set; } = 1;

    [Export]
    public Color PickupColor { get; set; } = new(0.38f, 0.72f, 0.67f, 1.0f);

    [Export(PropertyHint.Range, "0.5,2.0,0.05")]
    public float VisualScale { get; set; } = 1.0f;

    public override string InteractionPrompt
    {
        get
        {
            ItemDefinition? item = ItemDefinition;
            if (item is null || Quantity < 1)
            {
                return "Pick up item";
            }

            return Quantity == 1
                ? $"Pick up {item.DisplayName}"
                : $"Pick up {item.DisplayName} x{Quantity}";
        }
    }

    public override void _Ready()
    {
        ItemDefinition item = ItemDefinition
            ?? throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' requires an item definition.");

        item.Validate();
        if (Quantity < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' requires a positive quantity.");
        }

        if (VisualScale <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' requires a positive visual scale.");
        }

        base._Ready();

        _interactionShape = GetNodeOrNull<CollisionShape2D>("%InteractionShape")
            ?? throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' requires an InteractionShape node.");

        _visualRoot = GetNodeOrNull<Node2D>("%VisualRoot")
            ?? throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' requires a VisualRoot node.");

        _bodyVisual = GetNodeOrNull<Polygon2D>("%BodyVisual")
            ?? throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' requires a BodyVisual node.");

        _coreVisual = GetNodeOrNull<Polygon2D>("%CoreVisual")
            ?? throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' requires a CoreVisual node.");

        _pickupLabel = GetNodeOrNull<Label>("%PickupLabel")
            ?? throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' requires a PickupLabel node.");

        _visualRoot.Scale = Vector2.One * VisualScale;
        _bodyVisual.Color = PickupColor;
        _coreVisual.Color = PickupColor.Darkened(0.52f);
        UpdateVisualText();
    }

    public override bool CanInteract(InteractionContext context)
    {
        return _isAvailable && !_isRemovalQueued && Quantity > 0;
    }

    public override InteractionResult Interact(InteractionContext context)
    {
        if (!CanInteract(context))
        {
            return InteractionResult.None;
        }

        if (context.Actor is not IInventoryOwner inventoryOwner)
        {
            return InteractionResult.WithMessage("This actor cannot carry items.");
        }

        ItemDefinition item = ItemDefinition
            ?? throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' lost its item definition.");

        int requestedQuantity = Quantity;
        InventoryAddResult result = inventoryOwner.Inventory.TryAdd(item, requestedQuantity);
        if (result.AddedNothing)
        {
            return InteractionResult.WithMessage("Inventory full.");
        }

        string collectedMessage = FormatCollectedMessage(item, result.AddedQuantity);
        if (result.WasFullyAdded)
        {
            BeginRemoval();
            return InteractionResult.WithMessage(collectedMessage);
        }

        Quantity = result.RemainingQuantity;
        UpdateVisualText();
        string remainingVerb = Quantity == 1 ? "remains" : "remain";
        return InteractionResult.WithMessage(
            $"{collectedMessage} {Quantity} {remainingVerb}.");
    }

    private void BeginRemoval()
    {
        _isAvailable = false;
        _isRemovalQueued = true;
        Quantity = 0;
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        _interactionShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        QueueFree();
    }

    private void UpdateVisualText()
    {
        ItemDefinition item = ItemDefinition
            ?? throw new InvalidOperationException(
                $"{nameof(WorldItemPickup2D)} on '{Name}' requires an item definition.");

        _pickupLabel.Text = Quantity == 1
            ? item.DisplayName
            : $"{item.DisplayName} x{Quantity}";
    }

    private static string FormatCollectedMessage(ItemDefinition item, int quantity)
    {
        return quantity == 1
            ? $"Picked up {item.DisplayName}."
            : $"Picked up {item.DisplayName} x{quantity}.";
    }
}
