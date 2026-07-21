using System;
using Godot;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Inventory;

namespace LineZero.UI;

public sealed partial class WeaponHudController : MarginContainer
{
    private Label _weaponNameLabel = null!;
    private Label _ammoLabel = null!;
    private Label _weaponStatusLabel = null!;
    private FirearmState? _firearmState;
    private InventoryModel? _inventory;
    private bool _isActorAlive = true;

    public override void _Ready()
    {
        _weaponNameLabel = RequireNode<Label>("%WeaponNameLabel");
        _ammoLabel = RequireNode<Label>("%WeaponAmmoLabel");
        _weaponStatusLabel = RequireNode<Label>("%WeaponStatusLabel");
        SetUnboundDisplay();
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    public void Bind(FirearmState firearmState, InventoryModel inventory)
    {
        ArgumentNullException.ThrowIfNull(firearmState);
        ArgumentNullException.ThrowIfNull(inventory);

        if (ReferenceEquals(_firearmState, firearmState) &&
            ReferenceEquals(_inventory, inventory))
        {
            Refresh();
            return;
        }

        Unbind();
        _firearmState = firearmState;
        _inventory = inventory;
        _firearmState.Changed += OnWeaponStateChanged;
        _inventory.Changed += OnInventoryChanged;
        Refresh();
    }

    public void SetActorAlive(bool isAlive)
    {
        _isActorAlive = isAlive;
        if (_firearmState is not null && _inventory is not null)
        {
            Refresh();
        }
    }

    private void Unbind()
    {
        if (_firearmState is not null)
        {
            _firearmState.Changed -= OnWeaponStateChanged;
        }

        if (_inventory is not null)
        {
            _inventory.Changed -= OnInventoryChanged;
        }

        _firearmState = null;
        _inventory = null;

        if (GodotObject.IsInstanceValid(_weaponNameLabel))
        {
            SetUnboundDisplay();
        }
    }

    private void OnWeaponStateChanged()
    {
        Refresh();
    }

    private void OnInventoryChanged()
    {
        Refresh();
    }

    private void Refresh()
    {
        FirearmState state = _firearmState
            ?? throw new InvalidOperationException(
                $"{nameof(WeaponHudController)} on '{Name}' has no firearm state.");
        InventoryModel inventory = _inventory
            ?? throw new InvalidOperationException(
                $"{nameof(WeaponHudController)} on '{Name}' has no inventory.");
        string ammoItemId = state.Definition.AmmoItemDefinition?.Id
            ?? throw new InvalidOperationException(
                "A validated firearm definition lost its ammunition item.");

        int reserveAmmo = inventory.CountByItemId(ammoItemId);
        _weaponNameLabel.Text = state.Definition.DisplayName.ToUpperInvariant();
        _ammoLabel.Text = $"{state.CurrentMagazineAmmo} / {reserveAmmo}";
        if (!_isActorAlive)
        {
            _weaponStatusLabel.Text = "COMBAT DISABLED";
            _weaponStatusLabel.Modulate = new Color(0.72f, 0.31f, 0.28f, 1.0f);
            return;
        }

        _weaponStatusLabel.Modulate = Colors.White;
        _weaponStatusLabel.Text = state.IsReloading
            ? "RELOADING..."
            : state.CurrentMagazineAmmo == 0
                ? "MAGAZINE EMPTY"
                : "READY";
    }

    private void SetUnboundDisplay()
    {
        _weaponNameLabel.Text = "NO WEAPON";
        _ammoLabel.Text = "-- / --";
        _weaponStatusLabel.Text = "UNAVAILABLE";
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(WeaponHudController)} on '{Name}' requires '{path}'.");
    }
}
