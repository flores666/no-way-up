using Godot;
using LineZero.World2D.Noise;

namespace LineZero.Tests.Fixtures;

public sealed partial class TestNoiseListener2D : Node2D, INoiseListener2D
{
    public Node ListenerNode => this;

    public Node2D ListenerNode2D => this;

    public bool CanReceiveNoise { get; set; } = true;

    public float HearingSensitivity { get; set; } = 1.0f;

    public float MinimumAudibleIntensity { get; set; } = 0.01f;

    public int ReceivedCount { get; private set; }

    public PerceivedNoise2D? LastNoise { get; private set; }

    public void ReceiveNoise(PerceivedNoise2D noise)
    {
        ReceivedCount++;
        LastNoise = noise;
    }

    public void Reset()
    {
        ReceivedCount = 0;
        LastNoise = null;
    }
}
