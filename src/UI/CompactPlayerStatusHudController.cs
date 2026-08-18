using System;
using Godot;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;
using LineZero.Gameplay.Movement;
using LineZero.World2D;
using LineZero.World2D.Combat;

namespace LineZero.UI;

public sealed partial class CompactPlayerStatusHudController : MarginContainer
{
	private Label _healthLabel = null!;
	private Label _staminaLabel = null!;
	private Label _ammoLabel = null!;
	private Label _flashlightLabel = null!;

	private PlayerController2D? _player;
	private PlayerWeaponController2D? _weaponController;
	private HealthModel? _health;
	private StaminaModel? _stamina;
	private InventoryModel? _inventory;
	private FirearmState? _firearm;
	private FlashlightModel? _flashlight;

	public override void _Ready()
	{
		_healthLabel = RequireNode<Label>("%CompactHealthLabel");
		_staminaLabel = RequireNode<Label>("%CompactStaminaLabel");
		_ammoLabel = RequireNode<Label>("%CompactAmmoLabel");
		_flashlightLabel = RequireNode<Label>("%CompactFlashlightLabel");

		SetUnboundDisplay();
	}

	public override void _ExitTree()
	{
		Unbind();
	}

	public void Bind(
		PlayerController2D player,
		PlayerWeaponController2D weaponController)
	{
		ArgumentNullException.ThrowIfNull(player);
		ArgumentNullException.ThrowIfNull(weaponController);

		if (_player is not null)
		{
			throw new InvalidOperationException(
				$"{nameof(CompactPlayerStatusHudController)} on '{Name}' is already bound.");
		}

		_player = player;
		_weaponController = weaponController;
		_health = player.Health;
		_stamina = player.Stamina;
		_inventory = player.Inventory;
		_firearm = weaponController.State;
		_flashlight = player.FlashlightController.Model;

		_health.Changed += OnHealthChanged;
		_stamina.Changed += OnStaminaChanged;
		_inventory.Changed += OnInventoryChanged;
		_firearm.Changed += OnFirearmChanged;
		_flashlight.Changed += OnFlashlightChanged;

		RefreshAll();
	}

	private void Unbind()
	{
		if (_health is not null)
		{
			_health.Changed -= OnHealthChanged;
		}

		if (_stamina is not null)
		{
			_stamina.Changed -= OnStaminaChanged;
		}

		if (_inventory is not null)
		{
			_inventory.Changed -= OnInventoryChanged;
		}

		if (_firearm is not null)
		{
			_firearm.Changed -= OnFirearmChanged;
		}

		if (_flashlight is not null)
		{
			_flashlight.Changed -= OnFlashlightChanged;
		}

		_player = null;
		_weaponController = null;
		_health = null;
		_stamina = null;
		_inventory = null;
		_firearm = null;
		_flashlight = null;
	}

	private void RefreshAll()
	{
		RefreshHealth();
		RefreshStamina();
		RefreshAmmo();
		RefreshFlashlight();
	}

	private void RefreshHealth()
	{
		HealthModel health = RequireBound(_health, "health");
		int current = Math.Clamp(health.CurrentHealth, 0, health.MaxHealth);
		_healthLabel.Text = $"{current} HP";
	}

	private void RefreshStamina()
	{
		StaminaModel stamina = RequireBound(_stamina, "stamina");
		int current = (int)Math.Round(stamina.Current, MidpointRounding.AwayFromZero);
		int maximum = Math.Max(
			1,
			(int)Math.Round(stamina.Maximum, MidpointRounding.AwayFromZero));
		_staminaLabel.Text = $"{Math.Clamp(current, 0, maximum)} STAMINA";
	}

	private void RefreshAmmo()
	{
		FirearmState firearm = RequireBound(_firearm, "firearm");
		if (firearm.Definition.ReloadMechanism == FirearmReloadMechanism.DetachableMagazine)
		{
			_ammoLabel.Text =
				$"{firearm.CurrentMagazineAmmo}/{firearm.Definition.MagazineCapacity}  " +
				$"{firearm.UsableSpareMagazineCount} MAG";
			return;
		}

		InventoryModel inventory = RequireBound(_inventory, "inventory");
		ItemDefinition ammoDefinition = firearm.Definition.AmmoItemDefinition
			?? throw new InvalidOperationException("Bound loose-round firearm has no ammunition definition.");
		int reserve = inventory.CountByItemId(ammoDefinition.Id);
		_ammoLabel.Text =
			$"{firearm.CurrentMagazineAmmo}/{firearm.Definition.MagazineCapacity} +{reserve} AMMO";
	}

	private void RefreshFlashlight()
	{
		FlashlightModel flashlight = RequireBound(_flashlight, "flashlight");
		InventoryModel inventory = RequireBound(_inventory, "inventory");
		int percent = Math.Clamp(
			(int)Math.Round(
				flashlight.NormalizedCharge * 100.0,
				MidpointRounding.AwayFromZero),
			0,
			100);
		int batteries = inventory.CountByItemId(FlashlightDefinition.RequiredBatteryItemId);
		string power = flashlight.IsOn ? "ON" : "OFF";
		_flashlightLabel.Text = $"{percent}% LIGHT {power}  {batteries} BAT";
	}

	private void OnHealthChanged(HealthChangeResult result) => RefreshHealth();

	private void OnStaminaChanged(StaminaChangeResult result) => RefreshStamina();

	private void OnInventoryChanged()
	{
		RefreshAmmo();
		RefreshFlashlight();
	}

	private void OnFirearmChanged() => RefreshAmmo();

	private void OnFlashlightChanged() => RefreshFlashlight();

	private void SetUnboundDisplay()
	{
		_healthLabel.Text = "-- HP";
		_staminaLabel.Text = "-- STAMINA";
		_ammoLabel.Text = "-- AMMO";
		_flashlightLabel.Text = "-- LIGHT";
	}

	private static T RequireBound<T>(T? value, string name) where T : class
	{
		return value ?? throw new InvalidOperationException(
			$"Compact player status HUD has no {name} binding.");
	}

	private TNode RequireNode<TNode>(string path) where TNode : Node
	{
		return GetNodeOrNull<TNode>(path)
			?? throw new InvalidOperationException(
				$"{nameof(CompactPlayerStatusHudController)} on '{Name}' requires '{path}'.");
	}
}
