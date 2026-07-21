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
        uint requiredMask =
            CollisionLayers2D.World |
            CollisionLayers2D.PlayerObjectiveSensor;
        if (CollisionLayer != 0 ||
            CollisionMask != requiredMask ||
            !Monitoring)
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

        BodyEntered += OnBodyEntered;
        AreaEntered += OnAreaEntered;
    }

    public override void _ExitTree()
    {
        BodyEntered -= OnBodyEntered;
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
            TryCompleteCurrentOccupant();
        }
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is PlayerController2D player)
        {
            TryComplete(player);
        }
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area is PlayerObjectiveSensor2D sensor &&
            sensor.IsLivingEligiblePlayer &&
            sensor.TryGetPlayer(out PlayerController2D? player) &&
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
            TryCompleteCurrentOccupant();
        }
    }

    private void TryCompleteCurrentOccupant()
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
                !GodotObject.IsInstanceValid(sensor) ||
                !sensor.IsInsideTree() ||
                !sensor.IsLivingEligiblePlayer)
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
            selectedSensor.TryGetPlayer(out PlayerController2D? sensorPlayer) &&
            sensorPlayer is not null)
        {
            TryComplete(sensorPlayer);
            return;
        }

        // BodyEntered remains a supported path. Inspecting current bodies once on
        // the stage transition prevents a player already touching the boundary
        // from losing completion solely because the smaller fixed sensor has not
        // crossed the edge yet.
        PlayerController2D? selectedPlayer = null;
        selectedInstanceId = ulong.MaxValue;
        foreach (Node2D body in GetOverlappingBodies())
        {
            if (body is not PlayerController2D player ||
                !GodotObject.IsInstanceValid(player) ||
                !player.IsInsideTree() ||
                player.Health.IsDead)
            {
                continue;
            }

            ulong instanceId = player.GetInstanceId();
            if (instanceId < selectedInstanceId)
            {
                selectedPlayer = player;
                selectedInstanceId = instanceId;
            }
        }

        if (selectedPlayer is not null)
        {
            TryComplete(selectedPlayer);
        }
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
