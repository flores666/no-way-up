using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Objectives;
using LineZero.World2D;

namespace LineZero.World2D.Levels;

public sealed partial class ObjectiveExitZone2D : Area2D
{
    private ObjectiveProgressModel? _objectives;
    private bool _completionPublished;

    public event Action<PlayerController2D>? EscapeCompleted;

    public override void _Ready()
    {
        if (CollisionLayer != 0 ||
            CollisionMask != CollisionLayers2D.PlayerObjectiveSensor ||
            !Monitoring ||
            Monitorable)
        {
            throw new InvalidOperationException(
                $"{nameof(ObjectiveExitZone2D)} on '{Name}' has invalid collision settings.");
        }

        CollisionShape2D shape = GetNodeOrNull<CollisionShape2D>("%ExitShape")
            ?? throw new InvalidOperationException(
                $"{nameof(ObjectiveExitZone2D)} on '{Name}' requires an ExitShape node.");
        if (shape.Shape is null || shape.Disabled)
        {
            throw new InvalidOperationException(
                $"{nameof(ObjectiveExitZone2D)} on '{Name}' requires an enabled shape.");
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
                $"{nameof(ObjectiveExitZone2D)} on '{Name}' already has objective state.");
        }

        _objectives = objectives;
        _objectives.Changed += OnObjectiveChanged;

        if (_objectives.CurrentStage == ObjectiveStage.ReachExit)
        {
            TryCompleteCurrentSensor();
        }
    }

    private void OnAreaEntered(Area2D area)
    {
        if (TryResolveEligibleSensor(area, out PlayerController2D? player) &&
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

        PlayerObjectiveSensor2D? selectedSensor = null;
        ulong selectedInstanceId = ulong.MaxValue;
        foreach (Area2D area in GetOverlappingAreas())
        {
            if (area is not PlayerObjectiveSensor2D sensor ||
                !TryResolveEligibleSensor(sensor, out _))
            {
                continue;
            }

            ulong instanceId = sensor.GetInstanceId();
            if (instanceId < selectedInstanceId)
            {
                selectedSensor = sensor;
                selectedInstanceId = instanceId;
            }
        }

        if (selectedSensor is not null &&
            TryResolveEligibleSensor(
                selectedSensor,
                out PlayerController2D? player) &&
            player is not null)
        {
            TryComplete(player);
        }
    }

    private static bool TryResolveEligibleSensor(
        Area2D area,
        out PlayerController2D? player)
    {
        player = null;
        if (area is not PlayerObjectiveSensor2D sensor ||
            !GodotObject.IsInstanceValid(sensor) ||
            !sensor.IsInsideTree() ||
            sensor.CollisionLayer != CollisionLayers2D.PlayerObjectiveSensor ||
            sensor.CollisionMask != 0 ||
            !sensor.IsLivingEligiblePlayer)
        {
            return false;
        }

        return sensor.TryGetPlayer(out player) && player is not null;
    }

    private void TryComplete(PlayerController2D player)
    {
        if (_completionPublished ||
            !GodotObject.IsInstanceValid(player) ||
            !player.IsInsideTree() ||
            player.Health.IsDead)
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
        SafeEventPublisher.Publish(EscapeCompleted, player, nameof(EscapeCompleted));
    }
}
