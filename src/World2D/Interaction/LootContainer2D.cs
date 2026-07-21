using System;
using Godot;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Noise;
using LineZero.World2D.Noise;

namespace LineZero.World2D.Interaction;

public sealed partial class LootContainer2D : Interactable2D, IInventoryContainer, INoiseEmitter2D
{
    private InventoryModel? _inventory;
    private StaticBody2D _solidBody = null!;
    private NoiseSystem2D? _noiseSystem;
    private bool _hasBeenSearched;

    [Export]
    public string ContainerDisplayName { get; set; } = "Storage Container";

    [Export(PropertyHint.Range, "1,128,1,or_greater")]
    public int SlotCapacity { get; set; } = 8;

    [Export]
    public Godot.Collections.Array<InventorySeedEntry> InitialContents { get; set; } = new();

    public override string InteractionPrompt => string.IsNullOrWhiteSpace(ContainerDisplayName)
        ? "Search container"
        : $"Search {ContainerDisplayName}";

    public bool HasBeenSearched => _hasBeenSearched;

    public InventoryModel Inventory => _inventory
        ?? throw new InvalidOperationException(
            $"{nameof(LootContainer2D)} on '{Name}' has no initialized inventory.");

    public override void _EnterTree()
    {
        InventoryComponent inventoryComponent = GetNodeOrNull<InventoryComponent>(
            "InventoryComponent")
            ?? throw new InvalidOperationException(
                $"{nameof(LootContainer2D)} on '{Name}' requires an InventoryComponent child.");

        inventoryComponent.SlotCapacity = SlotCapacity;
        inventoryComponent.InitialContents = InitialContents;
    }

    public override void _Ready()
    {
        if (string.IsNullOrWhiteSpace(ContainerDisplayName))
        {
            throw new InvalidOperationException(
                $"{nameof(LootContainer2D)} on '{Name}' requires a display name.");
        }

        if (SlotCapacity < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(LootContainer2D)} on '{Name}' requires a positive slot capacity.");
        }

        base._Ready();

        InventoryComponent inventoryComponent = GetNodeOrNull<InventoryComponent>(
            "%InventoryComponent")
            ?? throw new InvalidOperationException(
                $"{nameof(LootContainer2D)} on '{Name}' requires an InventoryComponent child.");

        Label containerLabel = GetNodeOrNull<Label>("%ContainerLabel")
            ?? throw new InvalidOperationException(
                $"{nameof(LootContainer2D)} on '{Name}' requires a ContainerLabel node.");

        _inventory = inventoryComponent.Inventory;
        containerLabel.Text = ContainerDisplayName;
        _solidBody = GetNodeOrNull<StaticBody2D>("SolidBody")
            ?? throw new InvalidOperationException(
                $"{nameof(LootContainer2D)} on '{Name}' requires a SolidBody node.");
    }

    public override void _ExitTree()
    {
        _noiseSystem = null;
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

        if (context.Actor is IInventoryOwner)
        {
            if (!_hasBeenSearched)
            {
                NoiseSystem2D noiseSystem = _noiseSystem
                    ?? throw new InvalidOperationException(
                        $"{nameof(LootContainer2D)} on '{Name}' has no bound noise system.");
                noiseSystem.EmitNoise(
                    context.Actor,
                    NoiseKind.Interaction,
                    1.0f,
                    GlobalPosition,
                    _solidBody,
                    $"Searching {ContainerDisplayName}");
                _hasBeenSearched = true;
            }

            return InteractionResult.None;
        }

        return InteractionResult.WithMessage("This actor cannot access inventories.");
    }

    public void BindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(LootContainer2D)} on '{Name}' already has a noise system.");
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
}
