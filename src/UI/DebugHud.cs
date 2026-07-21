using System;
using Godot;
using LineZero.World2D;

namespace LineZero.UI;

public sealed partial class DebugHud : MarginContainer
{
    private const double RefreshIntervalSeconds = 0.1;

    private Label _statsLabel = null!;
    private PlayerController2D? _player;
    private double _timeUntilRefresh;

    public override void _Ready()
    {
        _statsLabel = GetNodeOrNull<Label>("%StatsLabel")
            ?? throw new InvalidOperationException(
                $"{nameof(DebugHud)} on '{Name}' requires a StatsLabel node.");
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _player is null)
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

    public void Initialize(PlayerController2D player)
    {
        ArgumentNullException.ThrowIfNull(player);

        _player = player;
        _timeUntilRefresh = 0.0;
        UpdateText();
    }

    private void UpdateText()
    {
        if (_player is null)
        {
            return;
        }

        Vector2 position = _player.GlobalPosition;
        string flashlightState = _player.IsFlashlightEnabled ? "ON" : "OFF";

        _statsLabel.Text =
            $"FPS: {Engine.GetFramesPerSecond():0}\n" +
            $"Position: {position.X:0.0}, {position.Y:0.0}\n" +
            $"Flashlight: {flashlightState}";
    }
}
