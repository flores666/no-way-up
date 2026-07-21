using System;
using Godot;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;

namespace LineZero.UI;

public sealed partial class InventoryPanelController : MarginContainer
{
    private Label _capacityLabel = null!;
    private ItemList _slotList = null!;
    private Label _selectedItemLabel = null!;
    private Label _descriptionLabel = null!;
    private Button _useButton = null!;

    private InventoryModel? _inventory;
    private int _selectedSlotIndex = -1;
    private bool _isToggleEnabled = true;
    private bool _isActorAlive = true;

    public bool IsToggleEnabled => _isToggleEnabled;

    public int SelectedSlotIndex => _selectedSlotIndex;

    public event Action<int>? UseRequested;

    public event Action<bool>? OpenStateChanged;

    public override void _Ready()
    {
        _capacityLabel = RequireNode<Label>("%CapacityLabel");
        _slotList = RequireNode<ItemList>("%SlotList");
        _selectedItemLabel = RequireNode<Label>("%SelectedItemLabel");
        _descriptionLabel = RequireNode<Label>("%DescriptionLabel");
        _useButton = RequireNode<Button>("%UseButton");

        _slotList.ItemSelected += OnSlotSelected;
        _useButton.Pressed += OnUsePressed;

        Visible = false;
        UpdateSelectionDetails();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_slotList))
        {
            _slotList.ItemSelected -= OnSlotSelected;
            _useButton.Pressed -= OnUsePressed;
        }

        UnbindInventory();
    }

    public override void _Input(InputEvent @event)
    {
        if (!_isToggleEnabled ||
            !@event.IsActionPressed("toggle_inventory") ||
            @event is InputEventKey { Echo: true })
        {
            return;
        }

        SetOpen(!Visible);
        GetViewport().SetInputAsHandled();
    }

    public void SetToggleEnabled(bool enabled)
    {
        _isToggleEnabled = enabled;
        if (!enabled)
        {
            Close();
        }
    }

    public void SetActorAlive(bool isAlive)
    {
        _isActorAlive = isAlive;
        if (!isAlive)
        {
            Close();
        }

        UpdateSelectionDetails();
    }

    public void Close()
    {
        SetOpen(false);
    }

    public void Bind(InventoryModel inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        if (ReferenceEquals(_inventory, inventory))
        {
            RefreshSlots();
            return;
        }

        UnbindInventory();
        _inventory = inventory;
        _inventory.Changed += OnInventoryChanged;
        RefreshSlots();
    }

    private void OnSlotSelected(long index)
    {
        int selectedIndex = checked((int)index);
        _selectedSlotIndex = _inventory is not null &&
                             selectedIndex >= 0 &&
                             selectedIndex < _inventory.Capacity
            ? selectedIndex
            : -1;
        UpdateSelectionDetails();
    }

    private void OnUsePressed()
    {
        InventoryModel? inventory = _inventory;
        if (!_isActorAlive ||
            inventory is null ||
            _selectedSlotIndex < 0 ||
            _selectedSlotIndex >= inventory.Capacity)
        {
            UpdateSelectionDetails();
            return;
        }

        InventorySlot slot = inventory.Slots[_selectedSlotIndex];
        if (slot.IsEmpty || slot.Item?.UseEffect is null)
        {
            UpdateSelectionDetails();
            return;
        }

        UseRequested?.Invoke(_selectedSlotIndex);
    }

    private void SetOpen(bool isOpen)
    {
        if (Visible == isOpen)
        {
            return;
        }

        Visible = isOpen;
        if (isOpen)
        {
            _slotList.GrabFocus();
        }

        OpenStateChanged?.Invoke(isOpen);
    }

    private void UnbindInventory()
    {
        if (_inventory is not null)
        {
            _inventory.Changed -= OnInventoryChanged;
        }

        _inventory = null;
        _selectedSlotIndex = -1;

        if (GodotObject.IsInstanceValid(_slotList))
        {
            _slotList.Clear();
        }

        UpdateSelectionDetails();
    }

    private void OnInventoryChanged()
    {
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        InventoryModel inventory = _inventory
            ?? throw new InvalidOperationException(
                $"{nameof(InventoryPanelController)} on '{Name}' is not bound to an inventory.");

        _selectedSlotIndex = FindNearestPopulatedSlot(inventory, _selectedSlotIndex);
        _slotList.Clear();

        int occupiedSlotCount = 0;
        for (int index = 0; index < inventory.Capacity; index++)
        {
            InventorySlot slot = inventory.Slots[index];
            if (slot.IsEmpty)
            {
                _slotList.AddItem($"[{index + 1}]  Empty");
                _slotList.SetItemCustomFgColor(
                    index,
                    new Color(0.52f, 0.57f, 0.58f, 1.0f));
                continue;
            }

            occupiedSlotCount++;
            ItemDefinition item = slot.Item
                ?? throw new InvalidOperationException(
                    "A populated inventory slot must reference an item definition.");
            _slotList.AddItem($"[{index + 1}]  {item.DisplayName} x{slot.Quantity}");
            _slotList.SetItemTooltip(index, item.Description);
        }

        _capacityLabel.Text = $"Slots: {occupiedSlotCount} / {inventory.Capacity}";
        if (_selectedSlotIndex >= 0)
        {
            _slotList.Select(_selectedSlotIndex);
        }

        UpdateSelectionDetails();
    }

    private void UpdateSelectionDetails()
    {
        InventoryModel? inventory = _inventory;
        if (inventory is null ||
            _selectedSlotIndex < 0 ||
            _selectedSlotIndex >= inventory.Capacity)
        {
            SetDetails(
                "NO ITEM SELECTED",
                "Select an occupied inventory slot to inspect it.",
                canUse: false);
            return;
        }

        InventorySlot slot = inventory.Slots[_selectedSlotIndex];
        if (slot.IsEmpty)
        {
            SetDetails(
                "EMPTY SLOT",
                "This slot contains no item.",
                canUse: false);
            return;
        }

        ItemDefinition item = slot.Item
            ?? throw new InvalidOperationException(
                "A populated inventory slot must reference an item definition.");
        SetDetails(
            $"{item.DisplayName} x{slot.Quantity}",
            item.Description,
            canUse: _isActorAlive && item.UseEffect is not null);
    }

    private void SetDetails(string itemName, string description, bool canUse)
    {
        if (!GodotObject.IsInstanceValid(_selectedItemLabel))
        {
            return;
        }

        _selectedItemLabel.Text = itemName;
        _descriptionLabel.Text = description;
        _useButton.Disabled = !canUse;
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
            for (int index = 0; index < inventory.Capacity; index++)
            {
                if (!inventory.Slots[index].IsEmpty)
                {
                    return index;
                }
            }

            return -1;
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
                $"{nameof(InventoryPanelController)} on '{Name}' requires '{path}'.");
    }
}
