using System;

namespace LineZero.Gameplay.Noise;

public interface INoiseEventSource
{
    event Action<NoiseEvent>? NoiseEventEmitted;
}
