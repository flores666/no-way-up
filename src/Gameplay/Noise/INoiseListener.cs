using Godot;

namespace LineZero.Gameplay.Noise;

public interface INoiseListener
{
    Node ListenerNode { get; }

    bool CanReceiveNoise { get; }

    float HearingSensitivity { get; }

    float MinimumAudibleIntensity { get; }
}
