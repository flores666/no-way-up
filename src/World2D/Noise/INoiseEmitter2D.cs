namespace LineZero.World2D.Noise;

public interface INoiseEmitter2D
{
    void BindNoiseSystem(NoiseSystem2D noiseSystem);

    void UnbindNoiseSystem(NoiseSystem2D noiseSystem);
}
