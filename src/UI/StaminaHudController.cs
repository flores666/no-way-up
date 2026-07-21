using System;
using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Movement;

namespace LineZero.UI;

public sealed partial class StaminaHudController : MarginContainer
{
    private static readonly Color ActiveColor = new(1.0f, 1.0f, 1.0f, 1.0f);
    private static readonly Color DisabledColor = new(0.48f, 0.5f, 0.5f, 0.82f);

    private ProgressBar _staminaBar = null!;
    private Label _staminaLabel = null!;
    private Label _movementModeLabel = null!;
    private StaminaModel? _stamina;
    private IMovementModeSource? _movementModeSource;
    private HealthModel? _health;
    private double _displayedCurrent = double.NaN;
    private double _displayedMaximum = double.NaN;
    private bool _isActorAlive;
    private double _minimumStaminaToStartSprint;

    public override void _Ready()
    {
        _staminaBar = RequireNode<ProgressBar>("%StaminaBar");
        _staminaLabel = RequireNode<Label>("%StaminaLabel");
        _movementModeLabel = RequireNode<Label>("%MovementModeLabel");

        _staminaBar.MinValue = 0.0;
        _staminaBar.ShowPercentage = false;
        _staminaLabel.Text = "STAMINA -- / --";
        _movementModeLabel.Text = "MODE: --";
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    public void Bind(
        StaminaModel stamina,
        IMovementModeSource movementModeSource,
        HealthModel health,
        double minimumStaminaToStartSprint)
    {
        ArgumentNullException.ThrowIfNull(stamina);
        ArgumentNullException.ThrowIfNull(movementModeSource);
        ArgumentNullException.ThrowIfNull(health);
        if (!double.IsFinite(minimumStaminaToStartSprint) ||
            minimumStaminaToStartSprint < 0.0 ||
            minimumStaminaToStartSprint > stamina.Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumStaminaToStartSprint),
                "Sprint restart threshold must be finite and within stamina bounds.");
        }

        if (_stamina is not null || _movementModeSource is not null || _health is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(StaminaHudController)} on '{Name}' is already bound.");
        }

        _stamina = stamina;
        _movementModeSource = movementModeSource;
        _health = health;
        _minimumStaminaToStartSprint = minimumStaminaToStartSprint;
        _isActorAlive = health.IsAlive;

        _stamina.Changed += OnStaminaChanged;
        _movementModeSource.MovementModeChanged += OnMovementModeChanged;
        _health.Died += OnActorDied;
        Refresh();
    }

    private void Unbind()
    {
        if (_stamina is not null)
        {
            _stamina.Changed -= OnStaminaChanged;
        }

        if (_movementModeSource is not null)
        {
            _movementModeSource.MovementModeChanged -= OnMovementModeChanged;
        }

        if (_health is not null)
        {
            _health.Died -= OnActorDied;
        }

        _stamina = null;
        _movementModeSource = null;
        _health = null;
        _displayedCurrent = double.NaN;
        _displayedMaximum = double.NaN;
        _minimumStaminaToStartSprint = 0.0;
        _isActorAlive = false;
    }

    private void OnStaminaChanged(StaminaChangeResult result)
    {
        if (_isActorAlive)
        {
            RefreshStamina();
        }
    }

    private void OnMovementModeChanged(
        MovementMode previousMode,
        MovementMode currentMode)
    {
        if (_isActorAlive)
        {
            RefreshMovementMode();
        }
    }

    private void OnActorDied(DamageInfo damage, HealthChangeResult result)
    {
        _isActorAlive = false;
        Refresh();
    }

    private void Refresh()
    {
        StaminaModel stamina = _stamina
            ?? throw new InvalidOperationException(
                $"{nameof(StaminaHudController)} on '{Name}' is not bound to stamina.");

        _staminaBar.MaxValue = stamina.Maximum;
        _staminaBar.Value = stamina.Current;
        if (!_isActorAlive)
        {
            Modulate = DisabledColor;
            _displayedCurrent = double.NaN;
            _displayedMaximum = double.NaN;
            _staminaLabel.Text = "STAMINA DISABLED";
            _movementModeLabel.Text = "MODE: --";
            return;
        }

        Modulate = ActiveColor;
        RefreshStamina();
        RefreshMovementMode();
    }

    private void RefreshStamina()
    {
        StaminaModel stamina = _stamina
            ?? throw new InvalidOperationException("Stamina HUD has no stamina binding.");
        _staminaBar.MaxValue = stamina.Maximum;
        _staminaBar.Value = stamina.Current;
        double displayedCurrent = GetDisplayedCurrent(
            stamina.Current,
            stamina.Maximum,
            _minimumStaminaToStartSprint);
        double displayedMaximum = Math.Round(
            stamina.Maximum,
            digits: 1,
            mode: MidpointRounding.AwayFromZero);
        if (displayedCurrent != _displayedCurrent ||
            displayedMaximum != _displayedMaximum)
        {
            _displayedCurrent = displayedCurrent;
            _displayedMaximum = displayedMaximum;
            _staminaLabel.Text =
                $"STAMINA {displayedCurrent:0.0} / {displayedMaximum:0.0}";
        }
    }

    private static double GetDisplayedCurrent(
        double current,
        double maximum,
        double sprintRestartThreshold)
    {
        const double displayScale = 10.0;
        double rounded = Math.Round(
            current,
            digits: 1,
            mode: MidpointRounding.AwayFromZero);
        bool crossesSprintThreshold =
            current < sprintRestartThreshold &&
            rounded >= sprintRestartThreshold;
        bool crossesMaximum = current < maximum && rounded >= maximum;
        if (!crossesSprintThreshold && !crossesMaximum)
        {
            return rounded;
        }

        return Math.Floor(current * displayScale) / displayScale;
    }

    private void RefreshMovementMode()
    {
        IMovementModeSource movementModeSource = _movementModeSource
            ?? throw new InvalidOperationException("Stamina HUD has no movement-mode binding.");
        _movementModeLabel.Text = movementModeSource.CurrentMovementMode switch
        {
            MovementMode.Walk => "MODE: WALK",
            MovementMode.Crouch => "MODE: CROUCH",
            MovementMode.Sprint => "MODE: SPRINT",
            MovementMode.Crawl => "MODE: CRAWL",
            _ => throw new InvalidOperationException("Unknown player movement mode.")
        };
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(StaminaHudController)} on '{Name}' requires '{path}'.");
    }
}
