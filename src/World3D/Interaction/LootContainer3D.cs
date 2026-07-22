using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Inventory;

namespace LineZero.World3D.Interaction;

public sealed partial class LootContainer3D : Interactable3D,
    IInventoryContainer
{
    private InventoryModel? _inventory;
    private StaticBody3D? _solidBody;
    private bool _hasBeenSearched;

    [Export]
    public string ContainerDisplayName { get; set; } = "Storage Container";

    [Export(PropertyHint.Range, "1,128,1")]
    public int SlotCapacity { get; set; } = 8;

    [Export]
    public Godot.Collections.Array<InventorySeedEntry> InitialContents { get; set; } = new();

    public override string InteractionPrompt =>
        $"Search {ContainerDisplayName}";

    public bool HasBeenSearched => _hasBeenSearched;

    public InventoryModel Inventory => _inventory
        ?? throw new InvalidOperationException(
            $"{nameof(LootContainer3D)} on '{Name}' has no inventory.");

    public StaticBody3D AcousticOriginCollider => _solidBody
        ?? throw new InvalidOperationException(
            $"{nameof(LootContainer3D)} on '{Name}' has no solid body.");

    public override CollisionObject3D? InteractionOccluder => _solidBody;

    public event Action<LootContainer3D>? FirstSearched;

    public override void _EnterTree()
    {
        InventoryComponent component =
            GetNodeOrNull<InventoryComponent>("InventoryComponent")
            ?? throw new InvalidOperationException(
                $"{nameof(LootContainer3D)} on '{Name}' requires InventoryComponent.");
        component.SlotCapacity = SlotCapacity;
        component.InitialContents = InitialContents;
    }

    public override void _Ready()
    {
        if (string.IsNullOrWhiteSpace(ContainerDisplayName) || SlotCapacity < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(LootContainer3D)} on '{Name}' has invalid configuration.");
        }

        base._Ready();
        InventoryComponent component =
            GetNodeOrNull<InventoryComponent>("%InventoryComponent")
            ?? throw new InvalidOperationException(
                $"{nameof(LootContainer3D)} on '{Name}' requires InventoryComponent.");
        _inventory = component.Inventory;
        _solidBody = GetNodeOrNull<StaticBody3D>("%SolidBody3D")
            ?? throw new InvalidOperationException(
                $"{nameof(LootContainer3D)} on '{Name}' requires SolidBody3D.");
    }

    public override bool CanInteract(InteractionContext context)
    {
        return _inventory is not null;
    }

    public override InteractionResult Interact(InteractionContext context)
    {
        if (!CanInteract(context))
        {
            return InteractionResult.None;
        }

        if (context.Actor is not IInventoryOwner)
        {
            return InteractionResult.WithMessage(
                "This actor cannot access inventories.");
        }

        if (!_hasBeenSearched)
        {
            _hasBeenSearched = true;
            SafeEventPublisher.Publish(
                FirstSearched,
                this,
                $"{nameof(LootContainer3D)}.{nameof(FirstSearched)}");
        }

        return InteractionResult.None;
    }
}
