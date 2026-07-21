using System;
using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Movement;
using LineZero.Gameplay.Noise;
using LineZero.World2D;
using LineZero.World2D.Noise;

namespace LineZero.UI;

public sealed partial class NoiseHudController : MarginContainer
{
    private static readonly Color SilentColor = new(0.55f, 0.68f, 0.65f, 1.0f);
    private static readonly Color LowColor = new(0.57f, 0.78f, 0.52f, 1.0f);
    private static readonly Color MediumColor = new(0.91f, 0.68f, 0.25f, 1.0f);
    private static readonly Color LoudColor = new(0.92f, 0.29f, 0.22f, 1.0f);

    private Label _noiseLabel = null!;
    private Timer _silenceTimer = null!;
    private NoiseSystem2D? _noiseSystem;
    private PlayerController2D? _player;
    private HealthModel? _playerHealth;
    private bool _isPlayerDead;

    [Export(PropertyHint.Range, "0.1,10.0,0.1,or_greater")]
    public double SilenceDelaySeconds { get; set; } = 1.2;

    public override void _Ready()
    {
        if (!double.IsFinite(SilenceDelaySeconds) || SilenceDelaySeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(NoiseHudController)} on '{Name}' requires a positive silence delay.");
        }

        _noiseLabel = GetNodeOrNull<Label>("%NoiseLabel")
            ?? throw new InvalidOperationException(
                $"{nameof(NoiseHudController)} on '{Name}' requires a NoiseLabel node.");
        _silenceTimer = GetNodeOrNull<Timer>("%SilenceTimer")
            ?? throw new InvalidOperationException(
                $"{nameof(NoiseHudController)} on '{Name}' requires a SilenceTimer node.");
        if (!_silenceTimer.OneShot)
        {
            throw new InvalidOperationException(
                $"{nameof(NoiseHudController)} on '{Name}' requires a one-shot timer.");
        }

        _silenceTimer.Timeout += OnSilenceTimerTimeout;
        SetSilent();
    }

    public override void _ExitTree()
    {
        Unbind();
        if (GodotObject.IsInstanceValid(_silenceTimer))
        {
            _silenceTimer.Timeout -= OnSilenceTimerTimeout;
        }
    }

    public void Bind(NoiseSystem2D noiseSystem, PlayerController2D player)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        ArgumentNullException.ThrowIfNull(player);
        if (_noiseSystem is not null || _player is not null || _playerHealth is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(NoiseHudController)} on '{Name}' is already bound.");
        }

        if (!GodotObject.IsInstanceValid(noiseSystem) ||
            !noiseSystem.IsInsideTree() ||
            !GodotObject.IsInstanceValid(player) ||
            !player.IsInsideTree())
        {
            throw new ArgumentException("Noise HUD dependencies must be active scene nodes.");
        }

        _noiseSystem = noiseSystem;
        _player = player;
        _playerHealth = player.Health;
        _isPlayerDead = _playerHealth.IsDead;
        _noiseSystem.NoiseEmitted += OnNoiseEmitted;
        _playerHealth.Died += OnPlayerDied;
        ResetToSilent();
    }

    public void ResetToSilent()
    {
        SetSilent();
    }

    private void Unbind()
    {
        if (_noiseSystem is not null && GodotObject.IsInstanceValid(_noiseSystem))
        {
            _noiseSystem.NoiseEmitted -= OnNoiseEmitted;
        }

        if (_playerHealth is not null)
        {
            _playerHealth.Died -= OnPlayerDied;
        }

        _noiseSystem = null;
        _player = null;
        _playerHealth = null;
        _isPlayerDead = false;
    }

    private void OnNoiseEmitted(NoiseOccurrence2D occurrence)
    {
        if (_isPlayerDead ||
            _playerHealth is null ||
            _playerHealth.IsDead ||
            _player is null ||
            !GodotObject.IsInstanceValid(_player) ||
            !IsPlayerSource(occurrence.Noise.Source, _player))
        {
            return;
        }

        switch (occurrence.Noise.Kind)
        {
            case NoiseKind.Footstep:
                switch (_player.CurrentMovementMode)
                {
                    case MovementMode.Sprint:
                        SetDisplay("NOISE: MEDIUM", MediumColor);
                        break;
                    case MovementMode.Walk:
                    case MovementMode.Crouch:
                    case MovementMode.Crawl:
                        SetDisplay("NOISE: LOW", LowColor);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown player movement mode.");
                }

                break;
            case NoiseKind.Interaction:
                SetDisplay("NOISE: MEDIUM", MediumColor);
                break;
            case NoiseKind.Gunshot:
                SetDisplay("NOISE: LOUD", LoudColor);
                break;
            default:
                throw new InvalidOperationException("Unknown player noise kind.");
        }

        _silenceTimer.Stop();
        _silenceTimer.Start(SilenceDelaySeconds);
    }

    private void OnSilenceTimerTimeout()
    {
        SetSilent();
    }

    private void OnPlayerDied(DamageInfo damage, HealthChangeResult result)
    {
        _isPlayerDead = true;
        ResetToSilent();
    }

    private void SetSilent()
    {
        if (GodotObject.IsInstanceValid(_silenceTimer))
        {
            _silenceTimer.Stop();
        }

        if (GodotObject.IsInstanceValid(_noiseLabel))
        {
            SetDisplay("NOISE: SILENT", SilentColor);
        }
    }

    private void SetDisplay(string text, Color color)
    {
        _noiseLabel.Text = text;
        _noiseLabel.Modulate = color;
    }

    private static bool IsPlayerSource(Node source, PlayerController2D player)
    {
        return ReferenceEquals(source, player) || player.IsAncestorOf(source);
    }
}
