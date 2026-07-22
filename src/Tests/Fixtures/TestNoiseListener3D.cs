using System;
using Godot;
using LineZero.World3D.Noise;

namespace LineZero.Tests.Fixtures;

public sealed partial class TestNoiseListener3D : Node3D, INoiseListener3D
{
    public Node ListenerNode => this;

    public Node3D ListenerNode3D => this;

    public bool CanReceiveNoise { get; set; } = true;

    public float HearingSensitivity { get; set; } = 1.0f;

    public float MinimumAudibleIntensity { get; set; } = 0.01f;

    public bool ThrowOnReceive { get; set; }

    public int ReceivedCount { get; private set; }

    public PerceivedNoise3D? LastNoise { get; private set; }

    public void ReceiveNoise(PerceivedNoise3D noise)
    {
        ArgumentNullException.ThrowIfNull(noise);
        if (ThrowOnReceive)
        {
            throw new InvalidOperationException("Expected 3D listener failure.");
        }

        ReceivedCount++;
        LastNoise = noise;
    }
}
