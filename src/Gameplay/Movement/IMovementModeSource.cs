using System;

namespace LineZero.Gameplay.Movement;

public interface IMovementModeSource
{
    MovementMode CurrentMovementMode { get; }

    event Action<MovementMode, MovementMode>? MovementModeChanged;
}
