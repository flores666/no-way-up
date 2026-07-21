using Godot;
using LineZero.Gameplay.Noise;

namespace LineZero.World2D.Noise;

public interface INoiseListener2D : INoiseListener
{
    Node2D ListenerNode2D { get; }

    void ReceiveNoise(PerceivedNoise2D noise);
}
