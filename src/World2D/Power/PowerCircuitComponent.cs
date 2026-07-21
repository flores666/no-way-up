using Godot;
using LineZero.Gameplay.Power;

namespace LineZero.World2D.Power;

public sealed partial class PowerCircuitComponent : Node
{
    public PowerCircuitModel Model { get; } = new();
}
