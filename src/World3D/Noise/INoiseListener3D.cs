using Godot;
using LineZero.Gameplay.Noise;

namespace LineZero.World3D.Noise;

public interface INoiseListener3D : INoiseListener
{
    Node3D ListenerNode3D { get; }

    void ReceiveNoise(PerceivedNoise3D noise);
}
