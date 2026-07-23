using System;
using Godot;
using LineZero.Gameplay.Movement;
using LineZero.World3D;
using LineZero.World3D.Presentation;

namespace LineZero.UI;

public sealed partial class DebugHud3D : CanvasLayer
{
    private Label _statsLabel = null!;
    private PlayerController3D? _player;
    private CameraOcclusionController3D? _cameraOcclusion;
    private PlayerVisualController3D? _playerVisual;
    private StaminaModel? _stamina;
    private string _activeSceneName = string.Empty;

    [Export]
    public bool HudEnabled { get; set; } = true;

    public override void _Ready()
    {
        _statsLabel = GetNodeOrNull<Label>("%StatsLabel3D")
            ?? throw new InvalidOperationException(
                $"{nameof(DebugHud3D)} on '{Name}' requires a StatsLabel3D node.");
        SetProcess(false);
        ApplyEnabledState();
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    public void Bind(
        PlayerController3D player,
        CameraOcclusionController3D cameraOcclusion,
        PlayerVisualController3D playerVisual,
        string activeSceneName)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(cameraOcclusion);
        ArgumentNullException.ThrowIfNull(playerVisual);
        if (string.IsNullOrWhiteSpace(activeSceneName))
        {
            throw new ArgumentException(
                "Active scene name must be non-empty.",
                nameof(activeSceneName));
        }

        if (_player is not null)
        {
            if (ReferenceEquals(_player, player) &&
                ReferenceEquals(_cameraOcclusion, cameraOcclusion) &&
                ReferenceEquals(_playerVisual, playerVisual) &&
                string.Equals(
                    _activeSceneName,
                    activeSceneName,
                    StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"{nameof(DebugHud3D)} on '{Name}' is already bound.");
        }

        _player = player;
        _cameraOcclusion = cameraOcclusion;
        _playerVisual = playerVisual;
        _stamina = player.Stamina;
        _activeSceneName = activeSceneName;
        player.MovementModeChanged += OnMovementModeChanged;
        player.PostureChanged += OnPostureChanged;
        player.ClearanceStateChanged += OnClearanceStateChanged;
        player.InputStateChanged += OnInputStateChanged;
        _stamina.Changed += OnStaminaChanged;
        cameraOcclusion.FadedOccluderCountChanged +=
            OnFadedOccluderCountChanged;
        playerVisual.DebugSnapshotChanged += OnVisualDebugSnapshotChanged;
        if (HudEnabled)
        {
            UpdateText();
        }
    }

    public void SetHudEnabled(bool enabled)
    {
        HudEnabled = enabled;
        ApplyEnabledState();
        if (enabled && _player is not null && _cameraOcclusion is not null)
        {
            UpdateText();
        }
    }

    private void ApplyEnabledState()
    {
        Visible = HudEnabled;
        SetProcess(false);
    }

    private void UpdateText()
    {
        if (_player is null ||
            _cameraOcclusion is null ||
            _playerVisual is null)
        {
            return;
        }

        _statsLabel.Text =
            $"Movement mode: {_player.CurrentMovementMode}\n" +
            $"Stamina: {_player.Stamina.Current:0.0} / " +
            $"{_player.Stamina.Maximum:0.0}\n" +
            $"Posture: {_player.CurrentPosture}\n" +
            $"Clearance: {_player.LastClearanceState}\n" +
            $"Gameplay input enabled: {_player.IsGameplayInputEnabled}\n" +
            $"Can accept gameplay input: {_player.CanAcceptGameplayInput}\n" +
            $"Terminal: {_player.IsTerminalState}\n" +
            $"Faded occluders: {_cameraOcclusion.FadedOccluderCount}\n" +
            $"Animation: {_playerVisual.CurrentState}\n" +
            $"Locomotion blend: " +
            $"({_playerVisual.LocalLocomotionBlend.X:0.00}, " +
            $"{_playerVisual.LocalLocomotionBlend.Y:0.00})\n" +
            $"Presentation action: {_playerVisual.ActiveAction}\n" +
            $"Visual source: " +
            $"{(_playerVisual.IsUsingDevelopmentFallback ? "fallback" : "imported")}\n" +
            $"Visual yaw: {Mathf.RadToDeg(_playerVisual.CurrentVisualYaw):0.0} deg\n" +
            $"Sockets valid: {_playerVisual.HasValidSocketHierarchy}\n" +
            $"Missing animation clips: {_playerVisual.MissingClipCount}\n" +
            $"Scene: {_activeSceneName}";
    }

    private void Unbind()
    {
        if (_player is not null)
        {
            _player.MovementModeChanged -= OnMovementModeChanged;
            _player.PostureChanged -= OnPostureChanged;
            _player.ClearanceStateChanged -= OnClearanceStateChanged;
            _player.InputStateChanged -= OnInputStateChanged;
        }

        if (_stamina is not null)
        {
            _stamina.Changed -= OnStaminaChanged;
        }

        if (_cameraOcclusion is not null)
        {
            _cameraOcclusion.FadedOccluderCountChanged -=
                OnFadedOccluderCountChanged;
        }

        if (_playerVisual is not null)
        {
            _playerVisual.DebugSnapshotChanged -= OnVisualDebugSnapshotChanged;
        }

        _player = null;
        _cameraOcclusion = null;
        _playerVisual = null;
        _stamina = null;
        _activeSceneName = string.Empty;
    }

    private void OnMovementModeChanged(
        MovementMode previousMode,
        MovementMode currentMode)
    {
        UpdateTextIfEnabled();
    }

    private void OnPostureChanged(
        MovementMode previousPosture,
        MovementMode currentPosture)
    {
        UpdateTextIfEnabled();
    }

    private void OnClearanceStateChanged(PostureClearanceState state)
    {
        UpdateTextIfEnabled();
    }

    private void OnInputStateChanged()
    {
        UpdateTextIfEnabled();
    }

    private void OnStaminaChanged(StaminaChangeResult result)
    {
        UpdateTextIfEnabled();
    }

    private void OnFadedOccluderCountChanged(int count)
    {
        UpdateTextIfEnabled();
    }

    private void OnVisualDebugSnapshotChanged(PlayerVisualDebugSnapshot snapshot)
    {
        UpdateTextIfEnabled();
    }

    private void UpdateTextIfEnabled()
    {
        if (HudEnabled)
        {
            UpdateText();
        }
    }
}
