using System;
using System.Collections.Generic;
using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Timing;

namespace LineZero.World3D.Hazards;

public sealed partial class DamageZone3D : Area3D
{
    private const int MaximumCatchUpTicksPerPhysicsUpdate = 4;

    private sealed class TrackedTarget
    {
        public TrackedTarget(
            PlayerHazardSensor3D sensor,
            HealthModel health,
            double intervalSeconds)
        {
            Sensor = sensor;
            Health = health;
            Timer = new PeriodicCatchUpTimer(
                intervalSeconds,
                MaximumCatchUpTicksPerPhysicsUpdate);
        }

        public PlayerHazardSensor3D Sensor { get; }

        public HealthModel Health { get; }

        public PeriodicCatchUpTimer Timer { get; }
    }

    private readonly List<TrackedTarget> _trackedTargets = new();

    [Export(PropertyHint.Range, "1,9999,1")]
    public int DamageAmount { get; set; } = 10;

    [Export(PropertyHint.Range, "0.05,60.0,0.05")]
    public double TickIntervalSeconds { get; set; } = 1.0;

    [Export]
    public string DamageKind { get; set; } = "Environmental";

    [Export]
    public bool ApplyDamageImmediately { get; set; } = true;

    public override void _Ready()
    {
        if (DamageAmount < 1 ||
            !double.IsFinite(TickIntervalSeconds) ||
            TickIntervalSeconds <= 0.0 ||
            string.IsNullOrWhiteSpace(DamageKind))
        {
            throw new InvalidOperationException(
                $"{nameof(DamageZone3D)} on '{Name}' has invalid damage tuning.");
        }

        CollisionShape3D damageShape =
            GetNodeOrNull<CollisionShape3D>("%DamageShape3D")
            ?? throw new InvalidOperationException(
                $"{nameof(DamageZone3D)} on '{Name}' requires DamageShape3D.");
        if (damageShape.Shape is null || damageShape.Disabled)
        {
            throw new InvalidOperationException(
                "DamageZone3D requires an enabled authored shape.");
        }

        if (CollisionLayer != CollisionLayers3D.HazardZone ||
            CollisionMask != CollisionLayers3D.PlayerHazardSensor ||
            !Monitoring)
        {
            throw new InvalidOperationException(
                "DamageZone3D has invalid dedicated collision settings.");
        }

        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
        SetPhysicsProcess(false);
    }

    public override void _ExitTree()
    {
        AreaEntered -= OnAreaEntered;
        AreaExited -= OnAreaExited;
        _trackedTargets.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        for (int index = _trackedTargets.Count - 1; index >= 0; index--)
        {
            TrackedTarget target = _trackedTargets[index];
            if (!IsTargetDamageable(target))
            {
                _trackedTargets.RemoveAt(index);
                continue;
            }

            PeriodicCatchUpResult schedule = target.Timer.Advance(delta);
            for (int tick = 0;
                 tick < schedule.DueTicks && IsTargetDamageable(target);
                 tick++)
            {
                ApplyDamage(target.Health);
            }

            if (!IsTargetDamageable(target))
            {
                _trackedTargets.RemoveAt(index);
            }
        }

        if (_trackedTargets.Count == 0)
        {
            SetPhysicsProcess(false);
        }
    }

    public void StopTracking(HealthModel health)
    {
        ArgumentNullException.ThrowIfNull(health);
        for (int index = _trackedTargets.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(_trackedTargets[index].Health, health))
            {
                _trackedTargets.RemoveAt(index);
            }
        }

        if (_trackedTargets.Count == 0)
        {
            SetPhysicsProcess(false);
        }
    }

    private void OnAreaEntered(Area3D area)
    {
        if (area is not PlayerHazardSensor3D sensor ||
            !sensor.TryGetHealth(out HealthModel? health) ||
            health is null ||
            !health.IsAlive ||
            !health.AcceptsDamage ||
            ContainsTarget(sensor, health))
        {
            return;
        }

        TrackedTarget target = new(sensor, health, TickIntervalSeconds);
        _trackedTargets.Add(target);
        SetPhysicsProcess(true);
        if (ApplyDamageImmediately)
        {
            ApplyDamage(health);
            if (!IsTargetDamageable(target))
            {
                _trackedTargets.Remove(target);
            }
        }
    }

    private void OnAreaExited(Area3D area)
    {
        if (area is not PlayerHazardSensor3D sensor)
        {
            return;
        }

        for (int index = _trackedTargets.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(_trackedTargets[index].Sensor, sensor))
            {
                _trackedTargets.RemoveAt(index);
            }
        }

        if (_trackedTargets.Count == 0)
        {
            SetPhysicsProcess(false);
        }
    }

    private bool ContainsTarget(
        PlayerHazardSensor3D sensor,
        HealthModel health)
    {
        for (int index = 0; index < _trackedTargets.Count; index++)
        {
            TrackedTarget target = _trackedTargets[index];
            if (ReferenceEquals(target.Sensor, sensor) ||
                ReferenceEquals(target.Health, health))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTargetDamageable(TrackedTarget target)
    {
        return GodotObject.IsInstanceValid(target.Sensor) &&
               target.Sensor.IsInsideTree() &&
               target.Health.IsAlive &&
               target.Health.AcceptsDamage;
    }

    private void ApplyDamage(HealthModel health)
    {
        health.ApplyDamage(new DamageInfo(DamageAmount, this, DamageKind));
    }
}
