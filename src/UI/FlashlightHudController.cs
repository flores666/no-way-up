using System;
using Godot;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Inventory;

namespace LineZero.UI;

public sealed partial class FlashlightHudController : MarginContainer
{
    private static readonly Color NormalStatusColor = new(0.78f, 0.92f, 0.86f, 1.0f);
    private static readonly Color LowStatusColor = new(0.94f, 0.72f, 0.28f, 1.0f);
    private static readonly Color CriticalStatusColor = new(0.95f, 0.42f, 0.28f, 1.0f);
    private static readonly Color DisabledStatusColor = new(0.55f, 0.57f, 0.56f, 1.0f);

    private Label _titleLabel = null!;
    private ProgressBar _chargeBar = null!;
    private Label _chargeLabel = null!;
    private Label _statusLabel = null!;
    private Label _batteryCountLabel = null!;
    private FlashlightModel? _flashlight;
    private InventoryModel? _inventory;
    private bool _isActorAlive = true;

    public override void _Ready()
    {
        _titleLabel = RequireNode<Label>("%FlashlightTitleLabel");
        _chargeBar = RequireNode<ProgressBar>("%FlashlightChargeBar");
        _chargeLabel = RequireNode<Label>("%FlashlightChargeLabel");
        _statusLabel = RequireNode<Label>("%FlashlightStatusLabel");
        _batteryCountLabel = RequireNode<Label>("%FlashlightBatteryCountLabel");
        SetUnboundDisplay();
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    public void Bind(FlashlightModel flashlight, InventoryModel inventory)
    {
        ArgumentNullException.ThrowIfNull(flashlight);
        ArgumentNullException.ThrowIfNull(inventory);

        if (ReferenceEquals(_flashlight, flashlight) &&
            ReferenceEquals(_inventory, inventory))
        {
            Refresh();
            return;
        }

        Unbind();
        _flashlight = flashlight;
        _inventory = inventory;
        _flashlight.Changed += OnFlashlightChanged;
        _inventory.Changed += OnInventoryChanged;
        Refresh();
    }

    public void SetActorAlive(bool isAlive)
    {
        _isActorAlive = isAlive;
        if (_flashlight is not null && _inventory is not null)
        {
            Refresh();
        }
    }

    private void Unbind()
    {
        if (_flashlight is not null)
        {
            _flashlight.Changed -= OnFlashlightChanged;
        }

        if (_inventory is not null)
        {
            _inventory.Changed -= OnInventoryChanged;
        }

        _flashlight = null;
        _inventory = null;
        if (GodotObject.IsInstanceValid(_titleLabel))
        {
            SetUnboundDisplay();
        }
    }

    private void OnFlashlightChanged()
    {
        Refresh();
    }

    private void OnInventoryChanged()
    {
        Refresh();
    }

    private void Refresh()
    {
        FlashlightModel flashlight = _flashlight
            ?? throw new InvalidOperationException("Flashlight HUD has no model binding.");
        InventoryModel inventory = _inventory
            ?? throw new InvalidOperationException("Flashlight HUD has no inventory binding.");
        string batteryItemId = FlashlightDefinition.RequiredBatteryItemId;

        _titleLabel.Text = "FLASHLIGHT";
        _chargeBar.MinValue = 0.0;
        _chargeBar.MaxValue = flashlight.MaximumCharge;
        _chargeBar.Value = flashlight.CurrentCharge;

        double displayedCharge = Math.Round(
            flashlight.CurrentCharge,
            digits: 1,
            mode: MidpointRounding.AwayFromZero);
        double displayedMaximum = Math.Round(
            flashlight.MaximumCharge,
            digits: 1,
            mode: MidpointRounding.AwayFromZero);
        _chargeLabel.Text = $"{displayedCharge:0.0} / {displayedMaximum:0.0}";
        _batteryCountLabel.Text = $"BATTERIES: {inventory.CountByItemId(batteryItemId)}";

        if (!_isActorAlive)
        {
            _statusLabel.Text = "DEAD";
            _statusLabel.Modulate = DisabledStatusColor;
            return;
        }

        if (flashlight.IsDepleted)
        {
            _statusLabel.Text = "EMPTY";
            _statusLabel.Modulate = CriticalStatusColor;
            return;
        }

        string powerState = flashlight.IsOn ? "ON" : "OFF";
        if (flashlight.IsCritical)
        {
            _statusLabel.Text = $"{powerState} · CRITICAL";
            _statusLabel.Modulate = CriticalStatusColor;
        }
        else if (flashlight.IsLow)
        {
            _statusLabel.Text = $"{powerState} · LOW";
            _statusLabel.Modulate = LowStatusColor;
        }
        else
        {
            _statusLabel.Text = powerState;
            _statusLabel.Modulate = flashlight.IsOn
                ? NormalStatusColor
                : DisabledStatusColor;
        }
    }

    private void SetUnboundDisplay()
    {
        _titleLabel.Text = "FLASHLIGHT";
        _chargeBar.MinValue = 0.0;
        _chargeBar.MaxValue = 100.0;
        _chargeBar.Value = 0.0;
        _chargeBar.ShowPercentage = false;
        _chargeLabel.Text = "--.- / --.-";
        _statusLabel.Text = "UNAVAILABLE";
        _statusLabel.Modulate = DisabledStatusColor;
        _batteryCountLabel.Text = "BATTERIES: --";
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(FlashlightHudController)} on '{Name}' requires '{path}'.");
    }
}
