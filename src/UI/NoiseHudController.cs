using System;
using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Noise;

namespace LineZero.UI;

public sealed partial class NoiseHudController : MarginContainer
{
    private static readonly Color SilentColor = new(0.55f, 0.68f, 0.65f, 1.0f);
    private static readonly Color LowColor = new(0.57f, 0.78f, 0.52f, 1.0f);
    private static readonly Color MediumColor = new(0.91f, 0.68f, 0.25f, 1.0f);
    private static readonly Color LoudColor = new(0.92f, 0.29f, 0.22f, 1.0f);

    private Label _noiseLabel = null!;
    private Timer _silenceTimer = null!;
    private INoiseEventSource? _noiseSource;
    private Node? _player;
    private HealthModel? _playerHealth;
    private bool _isPlayerDead;

    [Export(PropertyHint.Range, "0.1,10.0,0.1,or_greater")]
    public double SilenceDelaySeconds { get; set; } = 1.2;

    [Export(PropertyHint.Range, "0.01,10.0,0.01,or_greater")]
    public float MediumIntensityThreshold { get; set; } = 1.5f;

    public override void _Ready()
    {
        if (!double.IsFinite(SilenceDelaySeconds) || SilenceDelaySeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(NoiseHudController)} on '{Name}' requires a positive silence delay.");
        }

        if (!float.IsFinite(MediumIntensityThreshold) || MediumIntensityThreshold <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(NoiseHudController)} on '{Name}' requires a positive finite " +
                "medium-intensity threshold.");
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

    public void Bind(
        INoiseEventSource noiseSource,
        Node player,
        HealthModel playerHealth)
    {
        ArgumentNullException.ThrowIfNull(noiseSource);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(playerHealth);
        if (_noiseSource is not null || _player is not null || _playerHealth is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(NoiseHudController)} on '{Name}' is already bound.");
        }

        if (noiseSource is not Node noiseNode ||
            !GodotObject.IsInstanceValid(noiseNode) ||
            !noiseNode.IsInsideTree() ||
            !GodotObject.IsInstanceValid(player) || !player.IsInsideTree())
        {
            throw new ArgumentException("Noise HUD dependencies must be active scene nodes.");
        }

        _noiseSource = noiseSource;
        _player = player;
        _playerHealth = playerHealth;
        _isPlayerDead = _playerHealth.IsDead;
        _noiseSource.NoiseEventEmitted += OnNoiseEmitted;
        _playerHealth.Died += OnPlayerDied;
        ResetToSilent();
    }

    public void ResetToSilent()
    {
        SetSilent();
    }

    private void Unbind()
    {
        if (_noiseSource is not null)
        {
            _noiseSource.NoiseEventEmitted -= OnNoiseEmitted;
        }

        if (_playerHealth is not null)
        {
            _playerHealth.Died -= OnPlayerDied;
        }

        _noiseSource = null;
        _player = null;
        _playerHealth = null;
        _isPlayerDead = false;
    }

    private void OnNoiseEmitted(NoiseEvent noise)
    {
        if (_isPlayerDead ||
            _playerHealth is null ||
            _playerHealth.IsDead ||
            _player is null ||
            !GodotObject.IsInstanceValid(_player) ||
            !IsPlayerSource(noise.Source, _player))
        {
            return;
        }

        NoisePresentation presentation = Classify(noise);
        SetDisplay(presentation.Text, presentation.Color);
        _silenceTimer.Stop();
        _silenceTimer.Start(SilenceDelaySeconds);
    }

    private NoisePresentation Classify(NoiseEvent noise)
    {
        return noise.Kind switch
        {
            NoiseKind.Gunshot => new NoisePresentation("NOISE: LOUD", LoudColor),
            NoiseKind.Footstep or NoiseKind.Interaction =>
                noise.Intensity >= MediumIntensityThreshold
                    ? new NoisePresentation("NOISE: MEDIUM", MediumColor)
                    : new NoisePresentation("NOISE: LOW", LowColor),
            _ => throw new InvalidOperationException("Unknown player noise kind."),
        };
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

    private static bool IsPlayerSource(Node source, Node player)
    {
        return ReferenceEquals(source, player) || player.IsAncestorOf(source);
    }

    private readonly record struct NoisePresentation(string Text, Color Color);
}
