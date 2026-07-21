using System;
using LineZero.Gameplay.Movement;

namespace LineZero.Tests.Fixtures;

public sealed class TestMovementModeSource : IMovementModeSource
{
    public MovementMode CurrentMovementMode { get; private set; } = MovementMode.Walk;

    public event Action<MovementMode, MovementMode>? MovementModeChanged;

    public void SetMode(MovementMode mode)
    {
        if (CurrentMovementMode == mode)
        {
            return;
        }

        MovementMode previous = CurrentMovementMode;
        CurrentMovementMode = mode;
        MovementModeChanged?.Invoke(previous, mode);
    }
}
