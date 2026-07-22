using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Objectives;

namespace LineZero.World3D.Objectives;

public sealed partial class ObjectiveExitZone3D : Area3D
{
    private const int MaximumOverlappingSensors = 8;

    private ObjectiveProgressModel? _objectives;
    private bool _completionPublished;

    public event Action<PlayerController3D>? EscapeCompleted;

    public override void _Ready()
    {
        CollisionShape3D shape = GetNodeOrNull<CollisionShape3D>("%ExitShape3D")
            ?? throw new InvalidOperationException(
                $"{nameof(ObjectiveExitZone3D)} on '{Name}' requires ExitShape3D.");
        if (shape.Shape is null || shape.Disabled ||
            CollisionLayer != CollisionLayers3D.ObjectiveZone ||
            CollisionMask != CollisionLayers3D.PlayerObjectiveSensor ||
            !Monitoring ||
            Monitorable)
        {
            throw new InvalidOperationException(
                $"{nameof(ObjectiveExitZone3D)} on '{Name}' has invalid dedicated collision settings.");
        }

        AreaEntered += OnAreaEntered;
    }

    public override void _ExitTree()
    {
        AreaEntered -= OnAreaEntered;
        if (_objectives is not null)
        {
            _objectives.Changed -= OnObjectiveChanged;
        }

        _objectives = null;
        EscapeCompleted = null;
    }

    public void BindObjectives(ObjectiveProgressModel objectives)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        if (_objectives is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(ObjectiveExitZone3D)} on '{Name}' is already bound.");
        }

        _objectives = objectives;
        _objectives.Changed += OnObjectiveChanged;
        if (_objectives.CurrentStage == ObjectiveStage.ReachExit)
        {
            TryCompleteCurrentSensor();
        }
    }

    private void OnAreaEntered(Area3D area)
    {
        if (TryResolveEligibleSensor(area, out PlayerController3D? player) &&
            player is not null)
        {
            TryComplete(player);
        }
    }

    private void OnObjectiveChanged(
        ObjectiveStage previousStage,
        ObjectiveStage currentStage)
    {
        _ = previousStage;
        if (currentStage == ObjectiveStage.ReachExit)
        {
            TryCompleteCurrentSensor();
        }
    }

    private void TryCompleteCurrentSensor()
    {
        if (_completionPublished ||
            _objectives?.CurrentStage != ObjectiveStage.ReachExit)
        {
            return;
        }

        Godot.Collections.Array<Area3D> overlappingAreas = GetOverlappingAreas();
        int inspectedCount = Math.Min(overlappingAreas.Count, MaximumOverlappingSensors);
        PlayerObjectiveSensor3D? selectedSensor = null;
        ulong selectedId = ulong.MaxValue;
        for (int index = 0; index < inspectedCount; index++)
        {
            if (overlappingAreas[index] is not PlayerObjectiveSensor3D sensor ||
                !TryResolveEligibleSensor(sensor, out _))
            {
                continue;
            }

            ulong sensorId = sensor.GetInstanceId();
            if (sensorId < selectedId)
            {
                selectedSensor = sensor;
                selectedId = sensorId;
            }
        }

        if (selectedSensor is not null &&
            TryResolveEligibleSensor(selectedSensor, out PlayerController3D? player) &&
            player is not null)
        {
            TryComplete(player);
        }
    }

    private static bool TryResolveEligibleSensor(
        Area3D area,
        out PlayerController3D? player)
    {
        player = null;
        if (area is not PlayerObjectiveSensor3D sensor ||
            !GodotObject.IsInstanceValid(sensor) ||
            !sensor.IsInsideTree() ||
            sensor.CollisionLayer != CollisionLayers3D.PlayerObjectiveSensor ||
            sensor.CollisionMask != 0 ||
            !sensor.IsLivingEligiblePlayer)
        {
            return false;
        }

        return sensor.TryGetPlayer(out player) && player is not null;
    }

    private void TryComplete(PlayerController3D player)
    {
        if (_completionPublished ||
            !GodotObject.IsInstanceValid(player) ||
            !player.IsInsideTree() ||
            player.Health.IsDead ||
            player.IsTerminalState)
        {
            return;
        }

        ObjectiveProgressModel? objectives = _objectives;
        if (objectives is null ||
            objectives.CurrentStage != ObjectiveStage.ReachExit ||
            !objectives.TryAdvanceTo(ObjectiveStage.Completed))
        {
            return;
        }

        _completionPublished = true;
        SafeEventPublisher.Publish(
            EscapeCompleted,
            player,
            $"{nameof(ObjectiveExitZone3D)}.{nameof(EscapeCompleted)}");
    }
}
