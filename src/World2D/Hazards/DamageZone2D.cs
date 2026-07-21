using System;
using System.Collections.Generic;
using Godot;
using LineZero.Gameplay.Health;

namespace LineZero.World2D.Hazards;

public sealed partial class DamageZone2D : Area2D
{
    private const int MaxCatchUpTicksPerPhysicsUpdate = 4;
    private const double TickComparisonEpsilonFactor = 1e-9;

    private sealed class TrackedTarget
    {
        public TrackedTarget(
            PlayerHazardSensor2D sensor,
            HealthModel health)
        {
            Sensor = sensor;
            Health = health;
        }

        public PlayerHazardSensor2D Sensor { get; }

        public HealthModel Health { get; }

        public double AccumulatedSeconds { get; set; }
    }

    private readonly List<TrackedTarget> _trackedTargets = new();

    [Export(PropertyHint.Range, "1,9999,1,or_greater")]
    public int DamageAmount { get; set; } = 10;

    [Export(PropertyHint.Range, "0.05,60.0,0.05,or_greater")]
    public double TickIntervalSeconds { get; set; } = 1.0;

    [Export]
    public string DamageKind { get; set; } = "Environmental";

    [Export]
    public bool ApplyDamageImmediately { get; set; } = true;

    public override void _Ready()
    {
        if (DamageAmount < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(DamageZone2D)} on '{Name}' requires positive damage.");
        }

        if (!double.IsFinite(TickIntervalSeconds) || TickIntervalSeconds <= 0.0)
        {
            throw new InvalidOperationException(
                $"{nameof(DamageZone2D)} on '{Name}' requires a positive finite tick interval.");
        }

        if (string.IsNullOrWhiteSpace(DamageKind))
        {
            throw new InvalidOperationException(
                $"{nameof(DamageZone2D)} on '{Name}' requires a damage kind.");
        }

        CollisionShape2D damageShape = GetNodeOrNull<CollisionShape2D>("%DamageShape")
            ?? throw new InvalidOperationException(
                $"{nameof(DamageZone2D)} on '{Name}' requires a DamageShape node.");

        if (damageShape.Shape is null || damageShape.Disabled)
        {
            throw new InvalidOperationException(
                $"{nameof(DamageZone2D)} on '{Name}' requires an enabled collision shape.");
        }

        if (CollisionLayer != 0 ||
            CollisionMask != CollisionLayers2D.PlayerHazardSensor ||
            !Monitoring)
        {
            throw new InvalidOperationException(
                $"{nameof(DamageZone2D)} on '{Name}' has invalid collision settings.");
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
        bool canAdvanceTimers = double.IsFinite(delta) && delta > 0.0;

        for (int index = _trackedTargets.Count - 1; index >= 0; index--)
        {
            TrackedTarget target = _trackedTargets[index];
            if (!IsTargetDamageable(target))
            {
                _trackedTargets.RemoveAt(index);
                continue;
            }

            if (!canAdvanceTimers)
            {
                continue;
            }

            double accumulatedSeconds = target.AccumulatedSeconds + delta;
            target.AccumulatedSeconds = double.IsFinite(accumulatedSeconds)
                ? accumulatedSeconds
                : TickIntervalSeconds * MaxCatchUpTicksPerPhysicsUpdate;

            ApplyDueTicks(target);
            if (!IsTargetDamageable(target))
            {
                _trackedTargets.Remove(target);
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

    private void OnAreaEntered(Area2D area)
    {
        if (area is not PlayerHazardSensor2D sensor)
        {
            return;
        }

        if (!sensor.TryGetHealth(out HealthModel? health) || health is null)
        {
            return;
        }

        if (!health.IsAlive ||
            !health.AcceptsDamage ||
            ContainsTarget(sensor, health))
        {
            return;
        }

        TrackedTarget target = new(sensor, health);
        _trackedTargets.Add(target);
        SetPhysicsProcess(true);

        if (ApplyDamageImmediately)
        {
            ApplyDamage(target.Health);
            if (!target.Health.IsAlive || !target.Health.AcceptsDamage)
            {
                _trackedTargets.Remove(target);
                if (_trackedTargets.Count == 0)
                {
                    SetPhysicsProcess(false);
                }
            }
        }
    }

    private void OnAreaExited(Area2D area)
    {
        if (area is not PlayerHazardSensor2D sensor)
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

    private void ApplyDueTicks(TrackedTarget target)
    {
        double comparisonEpsilon = Math.Max(
            double.Epsilon,
            TickIntervalSeconds * TickComparisonEpsilonFactor);
        int appliedTicks = 0;

        while (appliedTicks < MaxCatchUpTicksPerPhysicsUpdate &&
               target.AccumulatedSeconds + comparisonEpsilon >= TickIntervalSeconds)
        {
            target.AccumulatedSeconds = Math.Max(
                0.0,
                target.AccumulatedSeconds - TickIntervalSeconds);
            ApplyDamage(target.Health);
            appliedTicks++;

            if (!target.Health.IsAlive || !target.Health.AcceptsDamage)
            {
                return;
            }
        }
    }

    private bool IsTargetDamageable(TrackedTarget target)
    {
        return GodotObject.IsInstanceValid(target.Sensor) &&
               target.Sensor.IsInsideTree() &&
               target.Health.IsAlive &&
               target.Health.AcceptsDamage;
    }

    private bool ContainsTarget(PlayerHazardSensor2D sensor, HealthModel health)
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

    private void ApplyDamage(HealthModel health)
    {
        DamageInfo damage = new(DamageAmount, this, DamageKind);
        health.ApplyDamage(damage);
    }
}
