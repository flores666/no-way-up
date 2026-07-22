using System;
using Godot;
using LineZero.World3D;

namespace LineZero.UI;

public sealed partial class DebugHud3D : CanvasLayer
{
    private const double RefreshIntervalSeconds = 0.15;

    private Label _statsLabel = null!;
    private PlayerController3D? _player;
    private PlayerAimController3D? _aimController;
    private string _activeSceneName = string.Empty;
    private double _timeUntilRefresh;

    [Export]
    public bool HudEnabled { get; set; } = true;

    public override void _Ready()
    {
        _statsLabel = GetNodeOrNull<Label>("%StatsLabel3D")
            ?? throw new InvalidOperationException(
                $"{nameof(DebugHud3D)} on '{Name}' requires a StatsLabel3D node.");
        ApplyEnabledState();
    }

    public override void _Process(double delta)
    {
        if (!HudEnabled || _player is null || _aimController is null)
        {
            return;
        }

        _timeUntilRefresh -= delta;
        if (_timeUntilRefresh > 0.0)
        {
            return;
        }

        _timeUntilRefresh = RefreshIntervalSeconds;
        UpdateText();
    }

    public void Bind(
        PlayerController3D player,
        PlayerAimController3D aimController,
        string activeSceneName)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(aimController);
        if (string.IsNullOrWhiteSpace(activeSceneName))
        {
            throw new ArgumentException(
                "Active scene name must be non-empty.",
                nameof(activeSceneName));
        }

        if (_player is not null &&
            (!ReferenceEquals(_player, player) ||
             !ReferenceEquals(_aimController, aimController)))
        {
            throw new InvalidOperationException(
                $"{nameof(DebugHud3D)} on '{Name}' is already bound.");
        }

        _player = player;
        _aimController = aimController;
        _activeSceneName = activeSceneName;
        _timeUntilRefresh = 0.0;
        if (HudEnabled)
        {
            UpdateText();
        }
    }

    public void SetHudEnabled(bool enabled)
    {
        HudEnabled = enabled;
        ApplyEnabledState();
        if (enabled && _player is not null && _aimController is not null)
        {
            _timeUntilRefresh = 0.0;
            UpdateText();
        }
    }

    private void ApplyEnabledState()
    {
        Visible = HudEnabled;
        SetProcess(HudEnabled);
    }

    private void UpdateText()
    {
        if (_player is null || _aimController is null)
        {
            return;
        }

        Vector3 position = _player.GlobalPosition;
        Vector2 horizontalVelocity = _player.HorizontalVelocity;
        string aimText = _aimController.HasValidAimPoint
            ? $"{_aimController.AimPoint.X:0.0}, " +
              $"{_aimController.AimPoint.Y:0.0}, " +
              $"{_aimController.AimPoint.Z:0.0}"
            : "invalid";

        _statsLabel.Text =
            $"FPS: {Engine.GetFramesPerSecond():0}\n" +
            $"Position: {position.X:0.0}, {position.Y:0.0}, {position.Z:0.0}\n" +
            $"Horizontal velocity: {horizontalVelocity.X:0.0}, {horizontalVelocity.Y:0.0}\n" +
            $"Grounded: {_player.IsOnFloor()}\n" +
            $"Aim point: {aimText}\n" +
            $"Scene: {_activeSceneName}";
    }
}
