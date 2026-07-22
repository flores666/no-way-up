using System;
using Godot;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Items;
using LineZero.Gameplay.Noise;
using LineZero.Gameplay.Objectives;
using LineZero.Gameplay.Power;
using LineZero.UI;
using LineZero.World3D;
using LineZero.World3D.Hazards;
using LineZero.World3D.Interaction;
using LineZero.World3D.Flashlight;
using LineZero.World3D.Noise;
using LineZero.World3D.Perception;
using LineZero.World3D.Combat;
using LineZero.World3D.Enemies;
using LineZero.World3D.Objectives;
using LineZero.World3D.Power;

namespace LineZero.Core;

public sealed partial class Main3D : Node3D
{
    private readonly ItemUseService _itemUseService = new();
    private readonly FlashlightBatteryService _flashlightBatteryService = new();
    private readonly ObjectiveProgressModel _objectiveProgress = new();

    private PlayerController3D? _player;
    private HealthModel? _playerHealth;
    private InventoryModel? _playerInventory;
    private PlayerAimController3D? _aimController;
    private PlayerInteractor3D? _playerInteractor;
    private PlayerWeaponController3D? _playerWeapon;
    private PlayerFlashlightController3D? _playerFlashlight;
    private PlayerFootstepNoiseEmitter3D? _footstepEmitter;
    private PlayerVisibilityController3D? _visibilityController;
    private NoiseSystem3D? _noiseSystem;
    private LootContainer3D? _emergencyCabinet;
    private MutantController3D? _mutant;
    private PowerController3D? _powerController;
    private FuseBox3D? _fuseBox;
    private EmergencyDoor3D? _emergencyExitDoor;
    private ObjectiveExitZone3D? _objectiveExitZone;
    private PowerControlledLight3D? _poweredExitLight;
    private InteractionPromptController? _interactionPrompt;
    private InteractionMessageController? _interactionMessage;
    private InventoryPanelController? _inventoryPanel;
    private LootTransferPanelController? _lootTransferPanel;
    private FlashlightHudController? _flashlightHud;
    private WeaponHudController? _weaponHud;
    private ObjectiveHudController? _objectiveHud;
    private EscapeCompletePanelController? _escapeCompletePanel;
    private Node? _activeContainerNode;
    private bool _externalGameplayInputEnabled = true;
    private bool _isInventoryOpen;
    private bool _isLootTransferOpen;
    private bool _isPlayerDead;
    private bool _isPrototypeCompleted;

    [Export]
    public bool EnableDebugHud3D { get; set; } = true;

    public bool IsInitialized { get; private set; }

    public bool IsTerminalState => _isPlayerDead || _isPrototypeCompleted;

    public bool IsPrototypeCompleted => _isPrototypeCompleted;

    public bool IsModalUiOpen => _isInventoryOpen || _isLootTransferOpen;

    public PlayerController3D Player => _player
        ?? throw new InvalidOperationException(
            $"{nameof(Main3D)} has not initialized its player.");

    public PlayerWeaponController3D Weapon => _playerWeapon
        ?? throw new InvalidOperationException(
            $"{nameof(Main3D)} has not initialized its weapon.");

    public MutantController3D Mutant => _mutant
        ?? throw new InvalidOperationException(
            $"{nameof(Main3D)} has not initialized its mutant.");

    public ObjectiveProgressModel Objectives => _objectiveProgress;

    public PowerCircuitModel PowerCircuit => _powerController?.Model
        ?? throw new InvalidOperationException(
            $"{nameof(Main3D)} has not initialized its power circuit.");

    public FuseBox3D FuseBox => _fuseBox
        ?? throw new InvalidOperationException(
            $"{nameof(Main3D)} has not initialized its fuse box.");

    public EmergencyDoor3D EmergencyExitDoor => _emergencyExitDoor
        ?? throw new InvalidOperationException(
            $"{nameof(Main3D)} has not initialized its emergency exit.");

    public ObjectiveExitZone3D ExitZone => _objectiveExitZone
        ?? throw new InvalidOperationException(
            $"{nameof(Main3D)} has not initialized its objective exit zone.");

    public PowerControlledLight3D PoweredExitLight => _poweredExitLight
        ?? throw new InvalidOperationException(
            $"{nameof(Main3D)} has not initialized its powered light.");

    public override void _Ready()
    {
        Node3D level = RequireNode<Node3D>("%TestLevel3D");
        PlayerController3D player = RequireNode<PlayerController3D>("%Player3D");
        TopDownCamera3D camera = RequireNode<TopDownCamera3D>("%TopDownCamera3D");
        CameraOcclusionController3D cameraOcclusion =
            RequireNode<CameraOcclusionController3D>(
                "%CameraOcclusionController3D");
        DebugHud3D debugHud = RequireNode<DebugHud3D>("%DebugHud3D");
        Node3D aimPointMarker = RequireNode<Node3D>("%AimPointMarker3D");
        Marker3D playerSpawn = level.GetNodeOrNull<Marker3D>("%PlayerSpawn3D")
            ?? throw new InvalidOperationException(
                "TestLevel3D requires a unique PlayerSpawn3D marker.");
        Node3D visualPivot =
            player.GetNodeOrNull<Node3D>("%VisualPivot3D")
            ?? throw new InvalidOperationException(
                "Player3D requires a unique VisualPivot3D node.");
        PlayerAimController3D aimController =
            player.GetNodeOrNull<PlayerAimController3D>(
                "%PlayerAimController3D")
            ?? throw new InvalidOperationException(
                "Player3D requires a unique PlayerAimController3D node.");
        PlayerInteractor3D playerInteractor =
            player.GetNodeOrNull<PlayerInteractor3D>(
                "%PlayerInteractionSensor3D")
            ?? throw new InvalidOperationException(
                "Player3D requires PlayerInteractionSensor3D.");
        PlayerHazardSensor3D hazardSensor =
            player.GetNodeOrNull<PlayerHazardSensor3D>(
                "%PlayerHazardSensor3D")
            ?? throw new InvalidOperationException(
                "Player3D requires PlayerHazardSensor3D.");
        PlayerFlashlightController3D playerFlashlight =
            player.GetNodeOrNull<PlayerFlashlightController3D>(
                "%PlayerFlashlightController3D")
            ?? throw new InvalidOperationException(
                "Player3D requires PlayerFlashlightController3D.");
        PlayerVisibilityController3D visibilityController =
            player.GetNodeOrNull<PlayerVisibilityController3D>(
                "%PlayerVisibilityController3D")
            ?? throw new InvalidOperationException(
                "Player3D requires PlayerVisibilityController3D.");
        PlayerVisibilitySensor3D visibilitySensor =
            player.GetNodeOrNull<PlayerVisibilitySensor3D>(
                "%PlayerVisibilitySensor3D")
            ?? throw new InvalidOperationException(
                "Player3D requires PlayerVisibilitySensor3D.");
        PlayerObjectiveSensor3D objectiveSensor =
            player.GetNodeOrNull<PlayerObjectiveSensor3D>(
                "%PlayerObjectiveSensor3D")
            ?? throw new InvalidOperationException(
                "Player3D requires PlayerObjectiveSensor3D.");
        PlayerFootstepNoiseEmitter3D footstepEmitter =
            player.GetNodeOrNull<PlayerFootstepNoiseEmitter3D>(
                "%PlayerFootstepNoiseEmitter3D")
            ?? throw new InvalidOperationException(
                "Player3D requires PlayerFootstepNoiseEmitter3D.");
        PlayerWeaponController3D playerWeapon =
            player.GetNodeOrNull<PlayerWeaponController3D>(
                "%PlayerWeaponController3D")
            ?? throw new InvalidOperationException(
                "Player3D requires PlayerWeaponController3D.");
        NoiseSystem3D noiseSystem = RequireNode<NoiseSystem3D>("%NoiseSystem3D");
        LootContainer3D emergencyCabinet =
            level.GetNodeOrNull<LootContainer3D>("%EmergencyCabinet3D")
            ?? throw new InvalidOperationException(
                "TestLevel3D requires EmergencyCabinet3D.");
        MutantController3D mutant =
            level.GetNodeOrNull<MutantController3D>("%TunnelMutant3D")
            ?? throw new InvalidOperationException(
                "TestLevel3D requires TunnelMutant3D.");
        PowerController3D powerController =
            level.GetNodeOrNull<PowerController3D>("%PowerController3D")
            ?? throw new InvalidOperationException(
                "TestLevel3D requires PowerController3D.");
        FuseBox3D fuseBox = level.GetNodeOrNull<FuseBox3D>(
                "%MaintenanceFuseBox3D")
            ?? throw new InvalidOperationException(
                "TestLevel3D requires MaintenanceFuseBox3D.");
        EmergencyDoor3D emergencyExitDoor =
            level.GetNodeOrNull<EmergencyDoor3D>("%EmergencyExitDoor3D")
            ?? throw new InvalidOperationException(
                "TestLevel3D requires EmergencyExitDoor3D.");
        ObjectiveExitZone3D objectiveExitZone =
            level.GetNodeOrNull<ObjectiveExitZone3D>("%ObjectiveExitZone3D")
            ?? throw new InvalidOperationException(
                "TestLevel3D requires ObjectiveExitZone3D.");
        PowerControlledLight3D poweredExitLight =
            level.GetNodeOrNull<PowerControlledLight3D>(
                "%ExitBayPoweredLight3D")
            ?? throw new InvalidOperationException(
                "TestLevel3D requires ExitBayPoweredLight3D.");

        InteractionPromptController interactionPrompt =
            RequireNode<InteractionPromptController>("%InteractionPrompt");
        InteractionMessageController interactionMessage =
            RequireNode<InteractionMessageController>("%InteractionMessage");
        InventoryPanelController inventoryPanel =
            RequireNode<InventoryPanelController>("%InventoryPanel");
        LootTransferPanelController lootTransferPanel =
            RequireNode<LootTransferPanelController>("%LootTransferPanel");
        HealthHudController healthHud =
            RequireNode<HealthHudController>("%HealthHud");
        StaminaHudController staminaHud =
            RequireNode<StaminaHudController>("%StaminaHud");
        FlashlightHudController flashlightHud =
            RequireNode<FlashlightHudController>("%FlashlightHud");
        VisibilityHudController visibilityHud =
            RequireNode<VisibilityHudController>("%VisibilityHud");
        NoiseHudController noiseHud =
            RequireNode<NoiseHudController>("%NoiseHud");
        WeaponHudController weaponHud =
            RequireNode<WeaponHudController>("%WeaponHud");
        ObjectiveHudController objectiveHud =
            RequireNode<ObjectiveHudController>("%ObjectiveHud");
        EscapeCompletePanelController escapeCompletePanel =
            RequireNode<EscapeCompletePanelController>("%EscapeCompletePanel");

        _player = player;
        _playerHealth = player.Health;
        _playerInventory = player.Inventory;
        _aimController = aimController;
        _playerInteractor = playerInteractor;
        _playerWeapon = playerWeapon;
        _playerFlashlight = playerFlashlight;
        _footstepEmitter = footstepEmitter;
        _visibilityController = visibilityController;
        _noiseSystem = noiseSystem;
        _emergencyCabinet = emergencyCabinet;
        _mutant = mutant;
        _powerController = powerController;
        _fuseBox = fuseBox;
        _emergencyExitDoor = emergencyExitDoor;
        _objectiveExitZone = objectiveExitZone;
        _poweredExitLight = poweredExitLight;
        _interactionPrompt = interactionPrompt;
        _interactionMessage = interactionMessage;
        _inventoryPanel = inventoryPanel;
        _lootTransferPanel = lootTransferPanel;
        _flashlightHud = flashlightHud;
        _weaponHud = weaponHud;
        _objectiveHud = objectiveHud;
        _escapeCompletePanel = escapeCompletePanel;
        _isPlayerDead = player.Health.IsDead;
        _isInventoryOpen = inventoryPanel.Visible;

        player.GlobalPosition = playerSpawn.GlobalPosition;
        camera.BindTarget(player);
        cameraOcclusion.Bind(camera, player);
        player.BindMovementCamera(camera);
        aimController.Bind(player, visualPivot, camera, aimPointMarker);
        playerInteractor.Bind(player, aimController);
        hazardSensor.Bind(player.Health);
        visibilityController.Initialize(player, playerFlashlight.Model, player.Health);
        visibilitySensor.Bind(visibilityController);
        objectiveSensor.Bind(player);
        footstepEmitter.Bind(player, player.Health, noiseSystem);
        playerWeapon.Initialize(
            player,
            aimController,
            player.Inventory,
            player.Health,
            noiseSystem);
        mutant.BindTarget(
            player,
            player.Health,
            visibilityController,
            noiseSystem);

        // Subscribe before stateful bindings so immediate synchronization cannot
        // lose progression or terminal events.
        _playerInventory.Changed += OnPlayerInventoryChanged;
        powerController.Model.PowerRestored += OnPowerRestored;
        emergencyExitDoor.Opened += OnEmergencyExitOpened;
        objectiveExitZone.EscapeCompleted += OnEscapeCompleted;
        fuseBox.BindPowerCircuit(powerController.Model);
        fuseBox.BindObjectives(_objectiveProgress);
        fuseBox.BindNoiseSystem(noiseSystem);
        emergencyExitDoor.BindPowerCircuit(powerController.Model);
        emergencyExitDoor.BindNoiseSystem(noiseSystem);
        poweredExitLight.BindPowerCircuit(powerController.Model);
        objectiveExitZone.BindObjectives(_objectiveProgress);

        playerInteractor.PromptChanged += interactionPrompt.SetPrompt;
        playerInteractor.MessageRequested += interactionMessage.ShowMessage;
        playerInteractor.InteractionCompleted += OnInteractionCompleted;
        player.PostureChangeRejected += interactionMessage.ShowMessage;
        player.Health.Died += OnPlayerDied;
        playerWeapon.MessageRequested += interactionMessage.ShowMessage;
        playerFlashlight.BatteryReplacementRequested +=
            OnBatteryReplacementRequested;
        emergencyCabinet.FirstSearched += OnContainerFirstSearched;
        inventoryPanel.UseRequested += OnInventoryUseRequested;
        inventoryPanel.OpenStateChanged += OnInventoryOpenStateChanged;
        lootTransferPanel.Closed += OnLootTransferPanelClosed;

        inventoryPanel.Bind(player.Inventory);
        inventoryPanel.SetActorAlive(player.Health.IsAlive);
        healthHud.Bind(player.Health);
        staminaHud.Bind(
            player.Stamina,
            player,
            player.Health,
            player.MinimumStaminaToStartSprint);
        flashlightHud.Bind(playerFlashlight.Model, player.Inventory);
        flashlightHud.SetActorAlive(player.Health.IsAlive);
        visibilityHud.Bind(visibilityController);
        noiseHud.Bind(noiseSystem, player, player.Health);
        weaponHud.Bind(playerWeapon.State, player.Inventory);
        weaponHud.SetActorAlive(player.Health.IsAlive);
        objectiveHud.Bind(_objectiveProgress);
        interactionPrompt.SetPrompt(playerInteractor.CurrentPrompt);

        debugHud.SetHudEnabled(EnableDebugHud3D);
        debugHud.Bind(player, aimController, SceneFilePath);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshGameplayInputState();
        SynchronizeFuseObjective();
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

        if (_player is not null)
        {
            if (_interactionMessage is not null)
            {
                _player.PostureChangeRejected -= _interactionMessage.ShowMessage;
            }

            if (_playerHealth is not null)
            {
                _playerHealth.Died -= OnPlayerDied;
            }

        }

        if (_playerInventory is not null)
        {
            _playerInventory.Changed -= OnPlayerInventoryChanged;
        }

        if (_playerFlashlight is not null)
        {
            _playerFlashlight.BatteryReplacementRequested -=
                OnBatteryReplacementRequested;
        }

        if (_playerWeapon is not null && _interactionMessage is not null)
        {
            _playerWeapon.MessageRequested -= _interactionMessage.ShowMessage;
        }

        if (_emergencyCabinet is not null &&
            GodotObject.IsInstanceValid(_emergencyCabinet))
        {
            _emergencyCabinet.FirstSearched -= OnContainerFirstSearched;
        }

        if (_powerController is not null)
        {
            _powerController.Model.PowerRestored -= OnPowerRestored;
        }

        if (_emergencyExitDoor is not null &&
            GodotObject.IsInstanceValid(_emergencyExitDoor))
        {
            _emergencyExitDoor.Opened -= OnEmergencyExitOpened;
        }

        if (_objectiveExitZone is not null &&
            GodotObject.IsInstanceValid(_objectiveExitZone))
        {
            _objectiveExitZone.EscapeCompleted -= OnEscapeCompleted;
        }

        if (_inventoryPanel is not null)
        {
            _inventoryPanel.UseRequested -= OnInventoryUseRequested;
            _inventoryPanel.OpenStateChanged -= OnInventoryOpenStateChanged;
        }

        if (_lootTransferPanel is not null)
        {
            _lootTransferPanel.Closed -= OnLootTransferPanelClosed;
        }

        _player = null;
        _playerHealth = null;
        _playerInventory = null;
        _aimController = null;
        _playerInteractor = null;
        _playerWeapon = null;
        _playerFlashlight = null;
        _footstepEmitter = null;
        _visibilityController = null;
        _noiseSystem = null;
        _emergencyCabinet = null;
        _mutant = null;
        _powerController = null;
        _fuseBox = null;
        _emergencyExitDoor = null;
        _objectiveExitZone = null;
        _poweredExitLight = null;
        _interactionPrompt = null;
        _interactionMessage = null;
        _inventoryPanel = null;
        _lootTransferPanel = null;
        _flashlightHud = null;
        _weaponHud = null;
        _objectiveHud = null;
        _escapeCompletePanel = null;
    }

    public void SetGameplayInputEnabled(bool enabled)
    {
        _externalGameplayInputEnabled = enabled;
        RefreshGameplayInputState();
    }

    public void SetPlayerDead(bool isDead)
    {
        if (!isDead)
        {
            RefreshGameplayInputState();
            return;
        }

        _isPlayerDead = true;
        EnterTerminalState();
    }

    public void SetPrototypeCompleted(bool isCompleted)
    {
        if (!isCompleted)
        {
            RefreshGameplayInputState();
            return;
        }

        _isPrototypeCompleted = true;
        Player.Health.DisableDamagePermanently();
        EnterTerminalState();
    }

    private void OnInteractionCompleted(
        IInteractable target,
        InteractionContext context,
        InteractionResult result)
    {
        if (IsTerminalState ||
            target is not IInventoryContainer container ||
            context.Actor is not IInventoryOwner inventoryOwner)
        {
            return;
        }

        if (target is not Node containerNode || !containerNode.IsInsideTree())
        {
            throw new InvalidOperationException(
                "A 3D inventory container must be an active scene node.");
        }

        OpenLootTransfer(container, containerNode, inventoryOwner.Inventory);
    }

    private void OpenLootTransfer(
        IInventoryContainer container,
        Node containerNode,
        InventoryModel playerInventory)
    {
        if (IsTerminalState)
        {
            return;
        }

        InventoryPanelController inventoryPanel = _inventoryPanel
            ?? throw new InvalidOperationException("Inventory panel is missing.");
        LootTransferPanelController lootTransferPanel = _lootTransferPanel
            ?? throw new InvalidOperationException("Loot panel is missing.");
        _isLootTransferOpen = true;
        inventoryPanel.Close();
        DetachActiveContainerNode();
        _activeContainerNode = containerNode;
        _activeContainerNode.TreeExiting += OnActiveContainerTreeExiting;
        lootTransferPanel.Open(
            playerInventory,
            container.Inventory,
            container.ContainerDisplayName);
        RefreshGameplayInputState();
    }

    private void OnInventoryUseRequested(int slotIndex)
    {
        if (IsTerminalState)
        {
            return;
        }

        PlayerController3D player = Player;
        ItemUseResult result = _itemUseService.TryUseFromSlot(
            player,
            player.Inventory,
            slotIndex);
        _interactionMessage?.ShowMessage(result.Message);
    }

    private void OnInventoryOpenStateChanged(bool isOpen)
    {
        _isInventoryOpen = isOpen;
        RefreshGameplayInputState();
    }

    private void OnBatteryReplacementRequested()
    {
        PlayerFlashlightController3D flashlight = _playerFlashlight
            ?? throw new InvalidOperationException("3D flashlight is missing.");
        BatteryReplacementResult result =
            _flashlightBatteryService.TryReplaceBattery(
                flashlight.Model,
                Player.Inventory,
                canReplace: !IsTerminalState &&
                            flashlight.IsFlashlightInputEnabled);
        _interactionMessage?.ShowMessage(result.Message);
    }

    private void OnContainerFirstSearched(LootContainer3D container)
    {
        NoiseSystem3D noiseSystem = _noiseSystem
            ?? throw new InvalidOperationException("3D noise system is missing.");
        noiseSystem.EmitNoise(
            Player,
            NoiseKind.Interaction,
            1.0f,
            container.GlobalPosition,
            container.AcousticOriginCollider,
            $"Searching {container.ContainerDisplayName}");
    }

    private void OnLootTransferPanelClosed()
    {
        _isLootTransferOpen = false;
        DetachActiveContainerNode();
        RefreshGameplayInputState();
    }

    private void OnPlayerInventoryChanged()
    {
        SynchronizeFuseObjective();
    }

    private void SynchronizeFuseObjective()
    {
        if (IsTerminalState ||
            _player is null ||
            _objectiveProgress.CurrentStage != ObjectiveStage.FindFuse)
        {
            return;
        }

        if (_player.Inventory.CountByItemId(
                FuseInstallationService.ReplacementFuseItemId) > 0)
        {
            _objectiveProgress.TryAdvanceTo(ObjectiveStage.RestorePower);
        }
    }

    private void OnPowerRestored()
    {
        if (IsTerminalState)
        {
            return;
        }

        if (_objectiveProgress.CurrentStage == ObjectiveStage.FindFuse)
        {
            _objectiveProgress.TryAdvanceTo(ObjectiveStage.RestorePower);
        }

        _objectiveProgress.TryAdvanceTo(ObjectiveStage.OpenExit);
    }

    private void OnEmergencyExitOpened(EmergencyDoor3D door)
    {
        if (IsTerminalState ||
            !ReferenceEquals(_emergencyExitDoor, door) ||
            !door.IsOpen)
        {
            return;
        }

        _objectiveProgress.TryAdvanceTo(ObjectiveStage.ReachExit);
    }

    private void OnEscapeCompleted(PlayerController3D player)
    {
        if (IsTerminalState ||
            !ReferenceEquals(_player, player) ||
            _objectiveProgress.CurrentStage != ObjectiveStage.Completed)
        {
            return;
        }

        _isPrototypeCompleted = true;
        player.Health.DisableDamagePermanently();
        EnterTerminalState();
        _escapeCompletePanel?.ShowCompletion();
    }

    private void OnPlayerDied(DamageInfo damage, HealthChangeResult result)
    {
        if (_isPlayerDead || _isPrototypeCompleted)
        {
            return;
        }

        _isPlayerDead = true;
        EnterTerminalState();
        _interactionMessage?.ShowMessage("You died.");
    }

    private void EnterTerminalState()
    {
        Player.EnterTerminalState();
        _playerFlashlight?.SetActorAlive(false);
        _flashlightHud?.SetActorAlive(false);
        _weaponHud?.SetActorAlive(false);
        _footstepEmitter?.StopAndClear();
        _mutant?.DisableTargeting();
        _inventoryPanel?.SetActorAlive(false);
        _inventoryPanel?.Close();
        _lootTransferPanel?.Close();
        _isInventoryOpen = false;
        _isLootTransferOpen = false;
        DetachActiveContainerNode();
        RefreshGameplayInputState();
    }

    private void RefreshGameplayInputState()
    {
        if (_player is null)
        {
            return;
        }

        bool progressionAllowsGameplay = !IsTerminalState;
        bool inventoryToggleEnabled =
            progressionAllowsGameplay &&
            _externalGameplayInputEnabled &&
            !_isLootTransferOpen;
        bool worldGameplayEnabled =
            inventoryToggleEnabled &&
            !_isInventoryOpen;
        _player.SetGameplayInputEnabled(worldGameplayEnabled);
        _aimController?.SetAimEnabled(worldGameplayEnabled);
        _playerInteractor?.SetGameplayInputEnabled(worldGameplayEnabled);
        _playerWeapon?.SetCombatInputEnabled(worldGameplayEnabled);
        _playerFlashlight?.SetFlashlightInputEnabled(worldGameplayEnabled);
        _footstepEmitter?.SetEmissionEnabled(worldGameplayEnabled);
        _inventoryPanel?.SetActorAlive(progressionAllowsGameplay);
        _inventoryPanel?.SetToggleEnabled(inventoryToggleEnabled);
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

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(Main3D)} requires '{path}'.");
    }
}
