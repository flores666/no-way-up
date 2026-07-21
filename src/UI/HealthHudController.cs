using System;
using Godot;
using LineZero.Gameplay.Health;

namespace LineZero.UI;

public sealed partial class HealthHudController : MarginContainer
{
    private ProgressBar _healthBar = null!;
    private Label _healthLabel = null!;
    private HealthModel? _health;

    public override void _Ready()
    {
        _healthBar = GetNodeOrNull<ProgressBar>("%HealthBar")
            ?? throw new InvalidOperationException(
                $"{nameof(HealthHudController)} on '{Name}' requires a HealthBar node.");

        _healthLabel = GetNodeOrNull<Label>("%HealthLabel")
            ?? throw new InvalidOperationException(
                $"{nameof(HealthHudController)} on '{Name}' requires a HealthLabel node.");

        _healthBar.MinValue = 0.0;
        _healthBar.ShowPercentage = false;
        _healthLabel.Text = "HEALTH -- / --";
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    public void Bind(HealthModel health)
    {
        ArgumentNullException.ThrowIfNull(health);

        if (ReferenceEquals(_health, health))
        {
            Refresh();
            return;
        }

        Unbind();
        _health = health;
        _health.Changed += OnHealthChanged;
        Refresh();
    }

    private void Unbind()
    {
        if (_health is not null)
        {
            _health.Changed -= OnHealthChanged;
        }

        _health = null;
    }

    private void OnHealthChanged(HealthChangeResult result)
    {
        Refresh();
    }

    private void Refresh()
    {
        HealthModel health = _health
            ?? throw new InvalidOperationException(
                $"{nameof(HealthHudController)} on '{Name}' is not bound to health.");

        _healthBar.MaxValue = health.MaxHealth;
        _healthBar.Value = health.CurrentHealth;
        _healthLabel.Text = $"HEALTH {health.CurrentHealth} / {health.MaxHealth}";
    }
}
