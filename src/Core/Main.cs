using System;
using Godot;
using LineZero.Data;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;
using LineZero.Gameplay.Objectives;
using LineZero.Gameplay.Power;
using LineZero.UI;
using LineZero.World2D;
using LineZero.World2D.Combat;
using LineZero.World2D.Interaction;
using LineZero.World2D.Levels;
using LineZero.World2D.Noise;

namespace LineZero.Core;

public sealed partial class Main : Node2D
{
    private readonly ItemUseService _itemUseService = new();
    private readonly FlashlightBatteryService _flashlightBatteryService = new();
    private readonly ObjectiveProgressModel _objectiveProgress = new();

    private PlayerController2D? _player;
    private PlayableLevelController2D? _activeLevel;
    private PlayerInteractor2D? _playerInteractor;
    private PlayerWeaponController2D? _playerWeapon;
    private PlayerFlashlightController2D? _playerFlashlight;
    private InteractionPromptController? _interactionPrompt;
    private InteractionMessageController? _interactionMessage;
    private InventoryPanelController? _inventoryPanel;
    private LootTransferPanelController? _lootTransferPanel;
    private WeaponHudController? _weaponHud;
    private FlashlightHudController? _flashlightHud;
    private EscapeCompletePanelController? _escapeCompletePanel;
    private PowerCircuitModel? _powerCircuit;
    private SlidingDoor2D? _emergencyExitDoor;
    private ObjectiveExitZone2D? _objectiveExitZone;
    private Node? _activeContainerNode;
    private bool _isLootTransferOpen;
    private bool _isInventoryOpen;
    private bool _isPlayerDead;
    private bool _isPrototypeCompleted;

    public bool IsInitialized { get; private set; }

    public override void _Ready()
    {
        PlayerController2D player = GetNodeOrNull<PlayerController2D>("%Player")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique Player node.");

        PlayableLevelController2D activeLevel =
            GetNodeOrNull<PlayableLevelController2D>("%PlayableLevel")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique PlayableLevel node.");

        NoiseSystem2D noiseSystem = GetNodeOrNull<NoiseSystem2D>("%NoiseSystem2D")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique NoiseSystem2D node.");

        DebugHud debugHud = GetNodeOrNull<DebugHud>("%DebugHud")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique DebugHud node.");

        PlayerInteractor2D playerInteractor = player.GetNodeOrNull<PlayerInteractor2D>(
            "%PlayerInteractor2D")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique PlayerInteractor2D node.");

        PlayerWeaponController2D playerWeapon =
            player.GetNodeOrNull<PlayerWeaponController2D>(
                "%PlayerWeaponController2D")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique PlayerWeaponController2D node.");

        PlayerFlashlightController2D playerFlashlight =
            player.GetNodeOrNull<PlayerFlashlightController2D>(
                "%PlayerFlashlightController2D")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique PlayerFlashlightController2D node.");

        InteractionPromptController interactionPrompt =
            GetNodeOrNull<InteractionPromptController>(
            "%InteractionPrompt")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique InteractionPrompt node.");

        InteractionMessageController interactionMessage =
            GetNodeOrNull<InteractionMessageController>(
            "%InteractionMessage")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique InteractionMessage node.");

        InventoryPanelController inventoryPanel =
            GetNodeOrNull<InventoryPanelController>(
            "%InventoryPanel")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique InventoryPanel node.");

        LootTransferPanelController lootTransferPanel =
            GetNodeOrNull<LootTransferPanelController>(
            "%LootTransferPanel")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique LootTransferPanel node.");

        HealthHudController healthHud = GetNodeOrNull<HealthHudController>(
            "%HealthHud")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique HealthHud node.");

        WeaponHudController weaponHud = GetNodeOrNull<WeaponHudController>(
            "%WeaponHud")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique WeaponHud node.");

        NoiseHudController noiseHud = GetNodeOrNull<NoiseHudController>("%NoiseHud")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique NoiseHud node.");

        StaminaHudController staminaHud = GetNodeOrNull<StaminaHudController>(
            "%StaminaHud")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique StaminaHud node.");

        FlashlightHudController flashlightHud =
            GetNodeOrNull<FlashlightHudController>("%FlashlightHud")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique FlashlightHud node.");

        VisibilityHudController visibilityHud =
            GetNodeOrNull<VisibilityHudController>("%VisibilityHud")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique VisibilityHud node.");

        ObjectiveHudController objectiveHud =
            GetNodeOrNull<ObjectiveHudController>("%ObjectiveHud")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique ObjectiveHud node.");

        EscapeCompletePanelController escapeCompletePanel =
            GetNodeOrNull<EscapeCompletePanelController>("%EscapeCompletePanel")
            ?? throw new InvalidOperationException(
                $"{nameof(Main)} requires a unique EscapeCompletePanel node.");

        _player = player;
        _activeLevel = activeLevel;
        _playerInteractor = playerInteractor;
        _playerWeapon = playerWeapon;
        _playerFlashlight = playerFlashlight;
        _interactionPrompt = interactionPrompt;
        _interactionMessage = interactionMessage;
        _inventoryPanel = inventoryPanel;
        _lootTransferPanel = lootTransferPanel;
        _weaponHud = weaponHud;
        _flashlightHud = flashlightHud;
        PowerCircuitModel powerCircuit = activeLevel.PowerCircuit.Model;
        SlidingDoor2D emergencyExitDoor = activeLevel.EmergencyExitDoor;
        ObjectiveExitZone2D objectiveExitZone = activeLevel.ExitZone;

        _escapeCompletePanel = escapeCompletePanel;
        _powerCircuit = powerCircuit;
        _emergencyExitDoor = emergencyExitDoor;
        _objectiveExitZone = objectiveExitZone;
        _isInventoryOpen = inventoryPanel.Visible;
        _isPlayerDead = player.Health.IsDead;

        player.BindNoiseSystem(noiseSystem);
        activeLevel.BindNoiseSystem(noiseSystem);
        activeLevel.BindMutantTargets(player, player, player);
        noiseHud.Bind(noiseSystem, player);
        PlayerMovementSettings movementSettings = player.MovementSettings
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerController2D)} requires movement settings before HUD binding.");
        staminaHud.Bind(
            player.Stamina,
            player,
            player.Health,
            movementSettings.MinimumStaminaToStartSprint);
        flashlightHud.Bind(playerFlashlight.Model, player.Inventory);
        flashlightHud.SetActorAlive(player.Health.IsAlive);
        visibilityHud.Bind(player.VisibilityController);
        objectiveHud.Bind(_objectiveProgress);

        // Subscribe before binding stateful level components. A late bind may
        // synchronize immediately and must not lose a terminal event.
        powerCircuit.PowerRestored += OnPowerRestored;
        emergencyExitDoor.Opened += OnEmergencyExitOpened;
        objectiveExitZone.EscapeCompleted += OnEscapeCompleted;

        activeLevel.FuseBox.BindPowerCircuit(powerCircuit);
        activeLevel.FuseBox.BindObjectives(_objectiveProgress);
        emergencyExitDoor.BindPowerCircuit(powerCircuit);
        for (int index = 0; index < activeLevel.PoweredLights.Count; index++)
        {
            activeLevel.PoweredLights[index].BindPowerCircuit(powerCircuit);
        }
        objectiveExitZone.BindObjectives(_objectiveProgress);

        playerInteractor.PromptChanged += interactionPrompt.SetPrompt;
        playerInteractor.MessageRequested += interactionMessage.ShowMessage;
        playerInteractor.InteractionCompleted += OnInteractionCompleted;
        player.PostureChangeRejected += interactionMessage.ShowMessage;
        playerWeapon.MessageRequested += interactionMessage.ShowMessage;
        playerFlashlight.BatteryReplacementRequested += OnBatteryReplacementRequested;
        lootTransferPanel.Closed += OnLootTransferPanelClosed;
        inventoryPanel.UseRequested += OnInventoryUseRequested;
        inventoryPanel.OpenStateChanged += OnInventoryOpenStateChanged;
        player.Health.Died += OnPlayerDied;
        player.Inventory.Changed += OnPlayerInventoryChanged;

        debugHud.Initialize(player);
        interactionPrompt.SetPrompt(playerInteractor.CurrentPrompt);

        IInventoryOwner inventoryOwner = player;
        inventoryPanel.Bind(inventoryOwner.Inventory);
        inventoryPanel.SetActorAlive(player.Health.IsAlive);

        IHealthOwner healthOwner = player;
        healthHud.Bind(healthOwner.Health);
        weaponHud.Bind(playerWeapon.State, inventoryOwner.Inventory);
        weaponHud.SetActorAlive(player.Health.IsAlive);
        SynchronizeFuseObjective();
        RefreshGameplayInputState();
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        IsInitialized = false;
        DetachActiveContainerNode();

        if (_playerInteractor is not null)
        {
            if (_interactionPrompt is not null)
            {
                _playerInteractor.PromptChanged -= _interactionPrompt.SetPrompt;
            }

            if (_interactionMessage is not null)
            {
                _playerInteractor.MessageRequested -= _interactionMessage.ShowMessage;
            }

            _playerInteractor.InteractionCompleted -= OnInteractionCompleted;
        }

        if (_lootTransferPanel is not null)
        {
            _lootTransferPanel.Closed -= OnLootTransferPanelClosed;
        }

        if (_inventoryPanel is not null)
        {
            _inventoryPanel.UseRequested -= OnInventoryUseRequested;
            _inventoryPanel.OpenStateChanged -= OnInventoryOpenStateChanged;
        }

        if (_playerWeapon is not null && _interactionMessage is not null)
        {
            _playerWeapon.MessageRequested -= _interactionMessage.ShowMessage;
        }

        if (_playerFlashlight is not null)
        {
            _playerFlashlight.BatteryReplacementRequested -= OnBatteryReplacementRequested;
        }

        if (_powerCircuit is not null)
        {
            _powerCircuit.PowerRestored -= OnPowerRestored;
        }

        if (_emergencyExitDoor is not null)
        {
            _emergencyExitDoor.Opened -= OnEmergencyExitOpened;
        }

        if (_objectiveExitZone is not null)
        {
            _objectiveExitZone.EscapeCompleted -= OnEscapeCompleted;
        }

        if (_player is not null)
        {
            if (_interactionMessage is not null)
            {
                _player.PostureChangeRejected -= _interactionMessage.ShowMessage;
            }

            _player.Health.Died -= OnPlayerDied;
            _player.Inventory.Changed -= OnPlayerInventoryChanged;
        }
    }

    private void OnInteractionCompleted(
        IInteractable target,
        InteractionContext context,
        InteractionResult result)
    {
        if (_isPlayerDead ||
            _isPrototypeCompleted ||
            target is not IInventoryContainer container ||
            context.Actor is not IInventoryOwner inventoryOwner)
        {
            return;
        }

        if (target is not Node containerNode || !containerNode.IsInsideTree())
        {
            throw new InvalidOperationException(
                "An inventory container interaction target must be an active scene node.");
        }

        OpenLootTransfer(container, containerNode, inventoryOwner.Inventory);
    }

    private void OpenLootTransfer(
        IInventoryContainer container,
        Node containerNode,
        InventoryModel playerInventory)
    {
        InventoryPanelController inventoryPanel = _inventoryPanel
            ?? throw new InvalidOperationException("The inventory panel is not initialized.");
        LootTransferPanelController lootTransferPanel = _lootTransferPanel
            ?? throw new InvalidOperationException("The loot panel is not initialized.");

        if (_isPlayerDead || _isPrototypeCompleted)
        {
            return;
        }

        _isLootTransferOpen = true;
        inventoryPanel.Close();
        RefreshGameplayInputState();

        DetachActiveContainerNode();
        _activeContainerNode = containerNode;
        _activeContainerNode.TreeExiting += OnActiveContainerTreeExiting;

        lootTransferPanel.Open(
            playerInventory,
            container.Inventory,
            container.ContainerDisplayName);
    }

    private void OnLootTransferPanelClosed()
    {
        _isLootTransferOpen = false;
        DetachActiveContainerNode();
        RefreshGameplayInputState();
    }

    private void OnInventoryUseRequested(int slotIndex)
    {
        if (_isPlayerDead || _isPrototypeCompleted)
        {
            return;
        }

        PlayerController2D player = _player
            ?? throw new InvalidOperationException("The player is not initialized.");
        InteractionMessageController interactionMessage = _interactionMessage
            ?? throw new InvalidOperationException("The interaction message UI is not initialized.");

        ItemUseResult result = _itemUseService.TryUseFromSlot(
            player,
            player.Inventory,
            slotIndex);
        interactionMessage.ShowMessage(result.Message);
    }

    private void OnInventoryOpenStateChanged(bool isOpen)
    {
        _isInventoryOpen = isOpen;
        RefreshGameplayInputState();
    }

    private void OnBatteryReplacementRequested()
    {
        PlayerController2D player = _player
            ?? throw new InvalidOperationException("The player is not initialized.");
        PlayerFlashlightController2D playerFlashlight = _playerFlashlight
            ?? throw new InvalidOperationException("The flashlight controller is not initialized.");
        InteractionMessageController interactionMessage = _interactionMessage
            ?? throw new InvalidOperationException("The interaction message UI is not initialized.");

        BatteryReplacementResult result = _flashlightBatteryService.TryReplaceBattery(
            playerFlashlight.Model,
            player.Inventory,
            canReplace: !_isPlayerDead &&
                        !_isPrototypeCompleted &&
                        playerFlashlight.IsFlashlightInputEnabled);
        interactionMessage.ShowMessage(result.Message);
    }

    private void OnPlayerInventoryChanged()
    {
        SynchronizeFuseObjective();
    }

    private void SynchronizeFuseObjective()
    {
        if (_isPlayerDead || _isPrototypeCompleted || _player is null ||
            _objectiveProgress.CurrentStage != ObjectiveStage.FindFuse)
        {
            return;
        }

        if (_player.Inventory.CountByItemId(FuseInstallationService.ReplacementFuseItemId) > 0)
        {
            _objectiveProgress.TryAdvanceTo(ObjectiveStage.RestorePower);
        }
    }

    private void OnPowerRestored()
    {
        if (_isPlayerDead || _isPrototypeCompleted)
        {
            return;
        }

        if (_objectiveProgress.CurrentStage == ObjectiveStage.FindFuse)
        {
            _objectiveProgress.TryAdvanceTo(ObjectiveStage.RestorePower);
        }

        _objectiveProgress.TryAdvanceTo(ObjectiveStage.OpenExit);
    }

    private void OnEmergencyExitOpened(SlidingDoor2D door)
    {
        if (_isPlayerDead ||
            _isPrototypeCompleted ||
            !ReferenceEquals(_emergencyExitDoor, door) ||
            !door.IsOpen)
        {
            return;
        }

        _objectiveProgress.TryAdvanceTo(ObjectiveStage.ReachExit);
    }

    private void OnEscapeCompleted(PlayerController2D player)
    {
        if (_isPrototypeCompleted || _isPlayerDead ||
            !ReferenceEquals(_player, player) ||
            _objectiveProgress.CurrentStage != ObjectiveStage.Completed)
        {
            return;
        }

        _isPrototypeCompleted = true;
        player.Health.DisableDamagePermanently();
        _activeLevel?.EnterTerminalPlayerState(player.Health);
        player.SetPlayerNoiseEnabled(false);
        _playerFlashlight?.TurnOff();
        _inventoryPanel?.Close();
        _lootTransferPanel?.Close();
        _isInventoryOpen = false;
        _isLootTransferOpen = false;
        DetachActiveContainerNode();
        RefreshGameplayInputState();
        _escapeCompletePanel?.ShowCompletion();
    }

    private void OnPlayerDied(DamageInfo damage, HealthChangeResult result)
    {
        if (_isPlayerDead || _isPrototypeCompleted)
        {
            return;
        }

        _isPlayerDead = true;
        _weaponHud?.SetActorAlive(false);
        _flashlightHud?.SetActorAlive(false);
        _playerFlashlight?.SetActorAlive(false);
        _inventoryPanel?.SetActorAlive(false);
        _inventoryPanel?.Close();
        _lootTransferPanel?.Close();
        _isLootTransferOpen = false;
        DetachActiveContainerNode();
        RefreshGameplayInputState();
        _interactionMessage?.ShowMessage("You died.");
    }

    private void OnActiveContainerTreeExiting()
    {
        _lootTransferPanel?.Close();
    }

    private void DetachActiveContainerNode()
    {
        if (_activeContainerNode is not null &&
            GodotObject.IsInstanceValid(_activeContainerNode))
        {
            _activeContainerNode.TreeExiting -= OnActiveContainerTreeExiting;
        }

        _activeContainerNode = null;
    }

    private void RefreshGameplayInputState()
    {
        bool progressionAllowsGameplay = !_isPlayerDead && !_isPrototypeCompleted;
        bool inventoryToggleEnabled = progressionAllowsGameplay && !_isLootTransferOpen;
        bool worldGameplayEnabled = inventoryToggleEnabled && !_isInventoryOpen;
        bool combatInputEnabled = worldGameplayEnabled;
        _player?.SetGameplayInputEnabled(worldGameplayEnabled);
        _playerInteractor?.SetGameplayInputEnabled(worldGameplayEnabled);
        _playerWeapon?.SetCombatInputEnabled(combatInputEnabled);
        _playerFlashlight?.SetFlashlightInputEnabled(worldGameplayEnabled);
        _inventoryPanel?.SetActorAlive(progressionAllowsGameplay);
        _inventoryPanel?.SetToggleEnabled(inventoryToggleEnabled);
    }
}
