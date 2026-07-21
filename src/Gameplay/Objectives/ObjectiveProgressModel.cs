using System;
using LineZero.Core.Events;

namespace LineZero.Gameplay.Objectives;

public sealed class ObjectiveProgressModel
{
    public ObjectiveStage CurrentStage { get; private set; } = ObjectiveStage.FindFuse;

    public bool IsCompleted => CurrentStage == ObjectiveStage.Completed;

    public event Action<ObjectiveStage, ObjectiveStage>? Changed;

    public bool TryAdvanceTo(ObjectiveStage nextStage)
    {
        if (!Enum.IsDefined(nextStage))
        {
            throw new ArgumentOutOfRangeException(nameof(nextStage));
        }

        if (IsCompleted || nextStage != (ObjectiveStage)((int)CurrentStage + 1))
        {
            return false;
        }

        ObjectiveStage previousStage = CurrentStage;
        CurrentStage = nextStage;
        SafeEventPublisher.Publish(
            Changed,
            previousStage,
            nextStage,
            $"{nameof(ObjectiveProgressModel)}.{nameof(Changed)}");
        return true;
    }
}
