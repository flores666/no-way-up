using System;

namespace LineZero.Gameplay.Perception;

public interface IVisibilityStateSource : IVisibilityTarget
{
    VisibilityState State { get; }

    event Action<VisibilityState>? VisibilityChanged;
}
