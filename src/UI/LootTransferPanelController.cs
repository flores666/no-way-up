using System;
using Godot;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;

namespace LineZero.UI;

public sealed partial class LootTransferPanelController : Control
{
    private ItemList _containerItemList = null!;
    private ItemList _playerItemList = null!;
    private Label _containerTitle = null!;
    private Label _playerTitle = null!;
    private Label _statusLabel = null!;
    private Button _takeOneButton = null!;
    private Button _takeStackButton = null!;
    private Button _storeOneButton = null!;
    private Button _storeStackButton = null!;
    private Button _closeButton = null!;

    private InventoryModel? _playerInventory;
    private InventoryModel? _containerInventory;
    private string _containerDisplayName = string.Empty;
    private int _containerSelectedIndex = -1;
    private int _playerSelectedIndex = -1;

    public bool IsOpen => Visible &&
                          _playerInventory is not null &&
                          _containerInventory is not null;

    public event Action? Closed;

    public override void _Ready()
    {
        _containerItemList = RequireNode<ItemList>("%ContainerItemList");
        _playerItemList = RequireNode<ItemList>("%PlayerItemList");
        _containerTitle = RequireNode<Label>("%ContainerTitle");
        _playerTitle = RequireNode<Label>("%PlayerTitle");
        _statusLabel = RequireNode<Label>("%StatusLabel");
        _takeOneButton = RequireNode<Button>("%TakeOneButton");
        _takeStackButton = RequireNode<Button>("%TakeStackButton");
        _storeOneButton = RequireNode<Button>("%StoreOneButton");
        _storeStackButton = RequireNode<Button>("%StoreStackButton");
        _closeButton = RequireNode<Button>("%CloseButton");

        _containerItemList.ItemSelected += OnContainerItemSelected;
        _playerItemList.ItemSelected += OnPlayerItemSelected;
        _takeOneButton.Pressed += OnTakeOnePressed;
        _takeStackButton.Pressed += OnTakeStackPressed;
        _storeOneButton.Pressed += OnStoreOnePressed;
        _storeStackButton.Pressed += OnStoreStackPressed;
        _closeButton.Pressed += Close;

        Visible = false;
        UpdateButtonStates();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_containerItemList))
        {
            _containerItemList.ItemSelected -= OnContainerItemSelected;
            _playerItemList.ItemSelected -= OnPlayerItemSelected;
            _takeOneButton.Pressed -= OnTakeOnePressed;
            _takeStackButton.Pressed -= OnTakeStackPressed;
            _storeOneButton.Pressed -= OnStoreOnePressed;
            _storeStackButton.Pressed -= OnStoreStackPressed;
            _closeButton.Pressed -= Close;
        }

        UnbindInventories();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen || !@event.IsActionPressed("ui_cancel"))
        {
            return;
        }

        Close();
        GetViewport().SetInputAsHandled();
    }

    public void Open(
        InventoryModel playerInventory,
        InventoryModel containerInventory,
        string containerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(playerInventory);
        ArgumentNullException.ThrowIfNull(containerInventory);

        if (ReferenceEquals(playerInventory, containerInventory))
        {
            throw new InvalidOperationException(
                "The loot panel requires two distinct inventories.");
        }

        if (string.IsNullOrWhiteSpace(containerDisplayName))
        {
            throw new ArgumentException(
                "The loot panel requires a container display name.",
                nameof(containerDisplayName));
        }

        UnbindInventories();

        _playerInventory = playerInventory;
        _containerInventory = containerInventory;
        _containerDisplayName = containerDisplayName.Trim();
        _containerSelectedIndex = FindFirstPopulatedSlot(containerInventory);
        _playerSelectedIndex = FindFirstPopulatedSlot(playerInventory);

        _playerInventory.Changed += OnPlayerInventoryChanged;
        _containerInventory.Changed += OnContainerInventoryChanged;

        _containerTitle.Text = _containerDisplayName;
        _playerTitle.Text = "Player Inventory";
        _statusLabel.Text = "Select a stack, then choose a transfer amount.";
        Visible = true;

        RefreshContainerList();
        RefreshPlayerList();
        _containerItemList.GrabFocus();
    }

    public void Close()
    {
        bool wasOpen = IsOpen;
        Visible = false;
        UnbindInventories();

        if (wasOpen)
        {
            Closed?.Invoke();
        }
    }

    private void OnContainerItemSelected(long index)
    {
        _containerSelectedIndex = checked((int)index);
        UpdateButtonStates();
    }

    private void OnPlayerItemSelected(long index)
    {
        _playerSelectedIndex = checked((int)index);
        UpdateButtonStates();
    }

    private void OnTakeOnePressed()
    {
        TransferFromSelectedSlot(takeFromContainer: true, transferWholeStack: false);
    }

    private void OnTakeStackPressed()
    {
        TransferFromSelectedSlot(takeFromContainer: true, transferWholeStack: true);
    }

    private void OnStoreOnePressed()
    {
        TransferFromSelectedSlot(takeFromContainer: false, transferWholeStack: false);
    }

    private void OnStoreStackPressed()
    {
        TransferFromSelectedSlot(takeFromContainer: false, transferWholeStack: true);
    }

    private void TransferFromSelectedSlot(bool takeFromContainer, bool transferWholeStack)
    {
        InventoryModel? source = takeFromContainer
            ? _containerInventory
            : _playerInventory;
        InventoryModel? destination = takeFromContainer
            ? _playerInventory
            : _containerInventory;
        int sourceSlotIndex = takeFromContainer
            ? _containerSelectedIndex
            : _playerSelectedIndex;

        if (source is null || destination is null)
        {
            return;
        }

        if (!TryGetPopulatedSlot(source, sourceSlotIndex, out InventorySlot? sourceSlot))
        {
            _statusLabel.Text = "Select a non-empty source slot.";
            UpdateButtonStates();
            return;
        }

        InventorySlot populatedSourceSlot = sourceSlot
            ?? throw new InvalidOperationException(
                "A successful slot lookup must return a slot.");
        ItemDefinition item = populatedSourceSlot.Item
            ?? throw new InvalidOperationException(
                "A populated inventory slot must reference an item definition.");
        int requestedQuantity = transferWholeStack ? populatedSourceSlot.Quantity : 1;
        InventoryTransferResult result = source.TryTransferTo(
            destination,
            sourceSlotIndex,
            requestedQuantity);

        if (result.TransferredNothing)
        {
            _statusLabel.Text = takeFromContainer
                ? $"Player inventory has no room for {item.DisplayName}."
                : $"{_containerDisplayName} has no room for {item.DisplayName}.";
            UpdateButtonStates();
            return;
        }

        string verb = takeFromContainer ? "Took" : "Stored";
        string message = $"{verb} {item.DisplayName} x{result.TransferredQuantity}.";
        if (!result.WasFullyTransferred && result.SourceQuantityAfterTransfer > 0)
        {
            string sourceName = takeFromContainer
                ? _containerDisplayName
                : "player inventory";
            string remainingVerb = result.SourceQuantityAfterTransfer == 1
                ? "remains"
                : "remain";
            message += $" {result.SourceQuantityAfterTransfer} {remainingVerb} in {sourceName}.";
        }

        _statusLabel.Text = message;
        UpdateButtonStates();
    }

    private void OnContainerInventoryChanged()
    {
        RefreshContainerList();
    }

    private void OnPlayerInventoryChanged()
    {
        RefreshPlayerList();
    }

    private void RefreshContainerList()
    {
        InventoryModel inventory = _containerInventory
            ?? throw new InvalidOperationException("No container inventory is bound.");
        RefreshList(inventory, _containerItemList, ref _containerSelectedIndex);
        UpdateButtonStates();
    }

    private void RefreshPlayerList()
    {
        InventoryModel inventory = _playerInventory
            ?? throw new InvalidOperationException("No player inventory is bound.");
        RefreshList(inventory, _playerItemList, ref _playerSelectedIndex);
        UpdateButtonStates();
    }

    private static void RefreshList(
        InventoryModel inventory,
        ItemList itemList,
        ref int selectedIndex)
    {
        selectedIndex = FindNearestPopulatedSlot(inventory, selectedIndex);
        itemList.Clear();

        for (int index = 0; index < inventory.Capacity; index++)
        {
            InventorySlot slot = inventory.Slots[index];
            string text = slot.IsEmpty
                ? $"[{index + 1}]  Empty"
                : $"[{index + 1}]  {slot.Item!.DisplayName} x{slot.Quantity}";
            itemList.AddItem(text);
        }

        if (selectedIndex >= 0)
        {
            itemList.Select(selectedIndex);
        }
    }

    private void UpdateButtonStates()
    {
        bool hasContainerSelection = _containerInventory is not null &&
                                     TryGetPopulatedSlot(
                                         _containerInventory,
                                         _containerSelectedIndex,
                                         out _);
        bool hasPlayerSelection = _playerInventory is not null &&
                                  TryGetPopulatedSlot(
                                      _playerInventory,
                                      _playerSelectedIndex,
                                      out _);

        if (GodotObject.IsInstanceValid(_takeOneButton))
        {
            _takeOneButton.Disabled = !hasContainerSelection;
            _takeStackButton.Disabled = !hasContainerSelection;
            _storeOneButton.Disabled = !hasPlayerSelection;
            _storeStackButton.Disabled = !hasPlayerSelection;
        }
    }

    private void UnbindInventories()
    {
        if (_playerInventory is not null)
        {
            _playerInventory.Changed -= OnPlayerInventoryChanged;
        }

        if (_containerInventory is not null)
        {
            _containerInventory.Changed -= OnContainerInventoryChanged;
        }

        _playerInventory = null;
        _containerInventory = null;
        _containerDisplayName = string.Empty;
        _containerSelectedIndex = -1;
        _playerSelectedIndex = -1;
        UpdateButtonStates();
    }

    private static bool TryGetPopulatedSlot(
        InventoryModel inventory,
        int index,
        out InventorySlot? slot)
    {
        if (index < 0 || index >= inventory.Capacity)
        {
            slot = null;
            return false;
        }

        slot = inventory.Slots[index];
        return !slot.IsEmpty;
    }

    private static int FindFirstPopulatedSlot(InventoryModel inventory)
    {
        for (int index = 0; index < inventory.Capacity; index++)
        {
            if (!inventory.Slots[index].IsEmpty)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindNearestPopulatedSlot(
        InventoryModel inventory,
        int preferredIndex)
    {
        if (preferredIndex >= 0 &&
            preferredIndex < inventory.Capacity &&
            !inventory.Slots[preferredIndex].IsEmpty)
        {
            return preferredIndex;
        }

        if (preferredIndex < 0 || preferredIndex >= inventory.Capacity)
        {
            return FindFirstPopulatedSlot(inventory);
        }

        for (int distance = 1; distance < inventory.Capacity; distance++)
        {
            int leftIndex = preferredIndex - distance;
            if (leftIndex >= 0 && !inventory.Slots[leftIndex].IsEmpty)
            {
                return leftIndex;
            }

            int rightIndex = preferredIndex + distance;
            if (rightIndex < inventory.Capacity && !inventory.Slots[rightIndex].IsEmpty)
            {
                return rightIndex;
            }
        }

        return -1;
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(LootTransferPanelController)} on '{Name}' requires '{path}'.");
    }
}
