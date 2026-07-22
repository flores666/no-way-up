using System;
using Godot;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;
using LineZero.World3D.Interaction;

namespace LineZero.World3D.Items;

public sealed partial class WorldItemPickup3D : Interactable3D
{
    private CollisionShape3D _interactionShape = null!;
    private MeshInstance3D _pickupMesh = null!;
    private bool _isAvailable = true;
    private bool _isRemovalQueued;

    [Export]
    public ItemDefinition? ItemDefinition { get; set; }

    [Export(PropertyHint.Range, "1,9999,1")]
    public int Quantity { get; set; } = 1;

    [Export]
    public Color PickupColor { get; set; } =
        new(0.38f, 0.72f, 0.67f, 1.0f);

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
                $"{nameof(WorldItemPickup3D)} on '{Name}' requires an item definition.");
        item.Validate();
        if (Quantity < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(WorldItemPickup3D)} on '{Name}' requires positive quantity.");
        }

        base._Ready();
        _interactionShape =
            GetNodeOrNull<CollisionShape3D>("%InteractionShape3D")
            ?? throw new InvalidOperationException("3D pickup shape is missing.");
        _pickupMesh = GetNodeOrNull<MeshInstance3D>("%PickupMesh3D")
            ?? throw new InvalidOperationException("3D pickup mesh is missing.");
        StandardMaterial3D material = new()
        {
            AlbedoColor = PickupColor,
            EmissionEnabled = true,
            Emission = PickupColor.Darkened(0.35f),
            EmissionEnergyMultiplier = 0.8f,
            Roughness = 0.72f
        };
        _pickupMesh.MaterialOverride = material;
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
            ?? throw new InvalidOperationException("3D pickup lost its item definition.");
        InventoryAddResult result = inventoryOwner.Inventory.TryAdd(item, Quantity);
        if (result.AddedNothing)
        {
            return InteractionResult.WithMessage("Inventory full.");
        }

        string collected = result.AddedQuantity == 1
            ? $"Picked up {item.DisplayName}."
            : $"Picked up {item.DisplayName} x{result.AddedQuantity}.";
        if (result.WasFullyAdded)
        {
            BeginRemoval();
            return InteractionResult.WithMessage(collected);
        }

        Quantity = result.RemainingQuantity;
        string verb = Quantity == 1 ? "remains" : "remain";
        return InteractionResult.WithMessage(
            $"{collected} {Quantity} {verb}.");
    }

    private void BeginRemoval()
    {
        _isAvailable = false;
        _isRemovalQueued = true;
        Quantity = 0;
        SetDeferred(Area3D.PropertyName.Monitorable, false);
        _interactionShape.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        _pickupMesh.Visible = false;
        QueueFree();
    }
}
