using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Noise;

namespace LineZero.World3D.Noise;

public sealed partial class NoiseSystem3D : Node3D, INoiseEventSource
{
    private const int MaximumOcclusionBarriers = 4;

    private readonly List<INoiseListener3D> _listeners = new();
    private readonly List<EmissionRecord> _emissionsThisProcessFrame = new();
    private readonly Godot.Collections.Array<Rid> _occlusionQueryExclusions = new();
    private readonly HashSet<ulong> _occlusionBarrierIds = new();

    private ulong _nextSequenceId = 1;
    private ulong _emissionProcessFrame = ulong.MaxValue;

    [Export(PropertyHint.Range, "0.1,100.0,0.1")]
    public float FootstepBaseRadius { get; set; } = 4.5f;

    [Export(PropertyHint.Range, "0.1,100.0,0.1")]
    public float InteractionBaseRadius { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "0.1,200.0,0.1")]
    public float GunshotBaseRadius { get; set; } = 20.0f;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float WallAttenuation { get; set; } = 0.5f;

    [Export(PropertyHint.Layers3DPhysics)]
    public uint OcclusionCollisionMask { get; set; } = CollisionLayers3D.World;

    public int ListenerCount => _listeners.Count;

    public event Action<NoiseOccurrence3D>? NoiseEmitted;

    public event Action<NoiseEvent>? NoiseEventEmitted;

    public event Action<INoiseListener3D, PerceivedNoise3D>? NoiseDelivered;

    public override void _Ready()
    {
        ValidatePositiveFinite(FootstepBaseRadius, nameof(FootstepBaseRadius));
        ValidatePositiveFinite(InteractionBaseRadius, nameof(InteractionBaseRadius));
        ValidatePositiveFinite(GunshotBaseRadius, nameof(GunshotBaseRadius));
        if (!float.IsFinite(WallAttenuation) ||
            WallAttenuation <= 0.0f ||
            WallAttenuation > 1.0f ||
            OcclusionCollisionMask != CollisionLayers3D.World)
        {
            throw new InvalidOperationException(
                $"{nameof(NoiseSystem3D)} on '{Name}' has invalid occlusion tuning.");
        }
    }

    public override void _ExitTree()
    {
        _listeners.Clear();
        _emissionsThisProcessFrame.Clear();
        _occlusionQueryExclusions.Clear();
        _occlusionBarrierIds.Clear();
        NoiseEmitted = null;
        NoiseEventEmitted = null;
        NoiseDelivered = null;
    }

    public void RegisterListener(INoiseListener3D listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        Node3D listenerNode = listener.ListenerNode3D;
        if (!GodotObject.IsInstanceValid(listenerNode) || !listenerNode.IsInsideTree())
        {
            throw new ArgumentException(
                "3D noise listeners must be active scene nodes.",
                nameof(listener));
        }

        ValidateListenerTuning(listener);
        for (int index = 0; index < _listeners.Count; index++)
        {
            if (ReferenceEquals(_listeners[index], listener))
            {
                throw new InvalidOperationException(
                    $"Noise listener '{listenerNode.Name}' is already registered.");
            }
        }

        _listeners.Add(listener);
    }

    public void UnregisterListener(INoiseListener3D listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        for (int index = _listeners.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(_listeners[index], listener))
            {
                _listeners.RemoveAt(index);
                return;
            }
        }
    }

    public NoiseOccurrence3D EmitNoise(
        Node source,
        NoiseKind kind,
        float intensity,
        Vector3 worldPosition,
        CollisionObject3D? originCollider = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidatePosition(worldPosition);
        if (originCollider is not null &&
            (!GodotObject.IsInstanceValid(originCollider) ||
             !originCollider.IsInsideTree()))
        {
            throw new ArgumentException(
                "A supplied 3D acoustic origin collider must be active.",
                nameof(originCollider));
        }

        NoiseEvent candidate = new(
            source,
            kind,
            intensity,
            _nextSequenceId,
            Time.GetTicksMsec() / 1000.0,
            description);
        BeginEmissionFrame(Engine.GetProcessFrames());
        NoiseOccurrence3D? duplicate = FindDuplicateEmission(
            source,
            kind,
            intensity,
            worldPosition,
            originCollider,
            description);
        if (duplicate is not null)
        {
            return duplicate;
        }

        if (_nextSequenceId == ulong.MaxValue)
        {
            throw new InvalidOperationException("3D noise sequence ID was exhausted.");
        }

        _nextSequenceId++;
        NoiseOccurrence3D occurrence = new(candidate, worldPosition);
        RememberEmission(occurrence, originCollider);
        SafeEventPublisher.Publish(
            NoiseEmitted,
            occurrence,
            $"{nameof(NoiseSystem3D)}.{nameof(NoiseEmitted)}");
        SafeEventPublisher.Publish(
            NoiseEventEmitted,
            occurrence.Noise,
            $"{nameof(NoiseSystem3D)}.{nameof(NoiseEventEmitted)}");
        DeliverNoise(occurrence, originCollider);
        return occurrence;
    }

    public float CalculateAttenuatedIntensity(float intensity, int barrierCount)
    {
        if (!float.IsFinite(intensity) || intensity <= 0.0f ||
            barrierCount < 0 || barrierCount > MaximumOcclusionBarriers)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intensity),
                "Noise attenuation inputs are invalid.");
        }

        return intensity * MathF.Pow(WallAttenuation, barrierCount);
    }

    private void DeliverNoise(
        NoiseOccurrence3D occurrence,
        CollisionObject3D? originCollider)
    {
        float baseRadius = GetBaseRadius(occurrence.Noise.Kind);
        for (int index = _listeners.Count - 1; index >= 0; index--)
        {
            INoiseListener3D listener = _listeners[index];
            Node3D listenerNode = listener.ListenerNode3D;
            if (!GodotObject.IsInstanceValid(listenerNode) || !listenerNode.IsInsideTree())
            {
                _listeners.RemoveAt(index);
                continue;
            }

            ValidateListenerTuning(listener);
            if (!listener.CanReceiveNoise ||
                IsOwnNoise(listenerNode, occurrence.Noise.Source))
            {
                continue;
            }

            float distance = listenerNode.GlobalPosition.DistanceTo(
                occurrence.WorldPosition);
            float unoccludedRadius =
                baseRadius * occurrence.Noise.Intensity * listener.HearingSensitivity;
            if (distance > unoccludedRadius)
            {
                continue;
            }

            int barriers = CountOccludingBarriers(
                occurrence,
                listenerNode,
                originCollider);
            float perceivedIntensity = CalculateAttenuatedIntensity(
                occurrence.Noise.Intensity,
                barriers);
            float effectiveRadius =
                baseRadius * perceivedIntensity * listener.HearingSensitivity;
            if (perceivedIntensity < listener.MinimumAudibleIntensity ||
                distance > effectiveRadius)
            {
                continue;
            }

            PerceivedNoise3D perceived = new(
                occurrence,
                perceivedIntensity,
                distance,
                effectiveRadius,
                barriers);
            try
            {
                listener.ReceiveNoise(perceived);
            }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "Noise listener '{0}' failed while receiving sequence {1}: {2}",
                    listenerNode.GetPath(),
                    occurrence.Noise.SequenceId,
                    exception);
                continue;
            }

            SafeEventPublisher.Publish(
                NoiseDelivered,
                listener,
                perceived,
                $"{nameof(NoiseSystem3D)}.{nameof(NoiseDelivered)}");
        }
    }

    private int CountOccludingBarriers(
        NoiseOccurrence3D occurrence,
        Node3D listenerNode,
        CollisionObject3D? originCollider)
    {
        if (occurrence.WorldPosition.DistanceSquaredTo(listenerNode.GlobalPosition) <=
            0.000001f)
        {
            return 0;
        }

        _occlusionQueryExclusions.Clear();
        _occlusionBarrierIds.Clear();
        AddExclusion(originCollider);
        AddExclusion(occurrence.Noise.Source as CollisionObject3D);
        AddExclusion(listenerNode as CollisionObject3D);
        PhysicsDirectSpaceState3D spaceState = GetWorld3D().DirectSpaceState;
        for (int index = 0; index < MaximumOcclusionBarriers; index++)
        {
            PhysicsRayQueryParameters3D query =
                PhysicsRayQueryParameters3D.Create(
                    occurrence.WorldPosition,
                    listenerNode.GlobalPosition,
                    OcclusionCollisionMask,
                    _occlusionQueryExclusions);
            query.CollideWithAreas = false;
            query.CollideWithBodies = true;
            query.HitFromInside = true;
            Godot.Collections.Dictionary hit = spaceState.IntersectRay(query);
            if (hit.Count == 0 ||
                !hit.TryGetValue("collider", out Variant colliderVariant) ||
                colliderVariant.AsGodotObject() is not CollisionObject3D collider)
            {
                break;
            }

            ulong colliderId = collider.GetInstanceId();
            if (!_occlusionBarrierIds.Add(colliderId))
            {
                break;
            }

            _occlusionQueryExclusions.Add(collider.GetRid());
        }

        return _occlusionBarrierIds.Count;
    }

    private void AddExclusion(CollisionObject3D? collider)
    {
        if (collider is null || !GodotObject.IsInstanceValid(collider))
        {
            return;
        }

        Rid rid = collider.GetRid();
        if (rid.IsValid && !_occlusionQueryExclusions.Contains(rid))
        {
            _occlusionQueryExclusions.Add(rid);
        }
    }

    private void BeginEmissionFrame(ulong processFrame)
    {
        if (_emissionProcessFrame == processFrame)
        {
            return;
        }

        _emissionProcessFrame = processFrame;
        _emissionsThisProcessFrame.Clear();
    }

    private NoiseOccurrence3D? FindDuplicateEmission(
        Node source,
        NoiseKind kind,
        float intensity,
        Vector3 position,
        CollisionObject3D? originCollider,
        string? description)
    {
        ulong sourceId = source.GetInstanceId();
        ulong colliderId = originCollider?.GetInstanceId() ?? 0;
        for (int index = 0; index < _emissionsThisProcessFrame.Count; index++)
        {
            EmissionRecord record = _emissionsThisProcessFrame[index];
            if (record.SourceId == sourceId &&
                record.Kind == kind &&
                Mathf.IsEqualApprox(record.Intensity, intensity) &&
                record.Position.IsEqualApprox(position) &&
                record.OriginColliderId == colliderId &&
                string.Equals(record.Description, description, StringComparison.Ordinal))
            {
                return record.Occurrence;
            }
        }

        return null;
    }

    private void RememberEmission(
        NoiseOccurrence3D occurrence,
        CollisionObject3D? originCollider)
    {
        _emissionsThisProcessFrame.Add(new EmissionRecord(
            occurrence,
            originCollider?.GetInstanceId() ?? 0));
    }

    private float GetBaseRadius(NoiseKind kind)
    {
        return kind switch
        {
            NoiseKind.Footstep => FootstepBaseRadius,
            NoiseKind.Interaction => InteractionBaseRadius,
            NoiseKind.Gunshot => GunshotBaseRadius,
            _ => throw new InvalidOperationException("Unknown noise kind.")
        };
    }

    private static bool IsOwnNoise(Node listener, Node source)
    {
        return ReferenceEquals(listener, source) ||
               listener.IsAncestorOf(source) ||
               source.IsAncestorOf(listener);
    }

    private static void ValidateListenerTuning(INoiseListener listener)
    {
        if (!float.IsFinite(listener.HearingSensitivity) ||
            listener.HearingSensitivity <= 0.0f ||
            !float.IsFinite(listener.MinimumAudibleIntensity) ||
            listener.MinimumAudibleIntensity <= 0.0f)
        {
            throw new InvalidOperationException("3D noise listener tuning is invalid.");
        }
    }

    private static void ValidatePosition(Vector3 position)
    {
        if (!float.IsFinite(position.X) ||
            !float.IsFinite(position.Y) ||
            !float.IsFinite(position.Z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "3D noise position must be finite.");
        }
    }

    private static void ValidatePositiveFinite(float value, string propertyName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new InvalidOperationException(
                $"Noise property '{propertyName}' must be finite and positive.");
        }
    }

    private readonly struct EmissionRecord
    {
        public EmissionRecord(
            NoiseOccurrence3D occurrence,
            ulong originColliderId)
        {
            Occurrence = occurrence;
            SourceId = occurrence.Noise.Source.GetInstanceId();
            Kind = occurrence.Noise.Kind;
            Intensity = occurrence.Noise.Intensity;
            Position = occurrence.WorldPosition;
            OriginColliderId = originColliderId;
            Description = occurrence.Noise.Description;
        }

        public NoiseOccurrence3D Occurrence { get; }

        public ulong SourceId { get; }

        public NoiseKind Kind { get; }

        public float Intensity { get; }

        public Vector3 Position { get; }

        public ulong OriginColliderId { get; }

        public string? Description { get; }
    }
}
