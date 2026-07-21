using System;
using System.Collections.Generic;
using Godot;
using LineZero.Gameplay.Noise;

namespace LineZero.World2D.Noise;

public sealed partial class NoiseSystem2D : Node2D
{
    private const int MaxOcclusionBarriers = 4;
    private const int MaxOcclusionQueryResults = 16;

    private readonly List<INoiseListener2D> _listeners = new();
    private readonly List<EmissionRecord> _emissionsThisProcessFrame = new();
    private readonly Godot.Collections.Array<Rid> _occlusionQueryExclusions = new();
    private readonly HashSet<OcclusionBarrierKey> _occlusionBarriers = new();
    private readonly SegmentShape2D _occlusionSegment = new();

    private ulong _nextSequenceId = 1;
    private ulong _emissionProcessFrame = ulong.MaxValue;

    [Export(PropertyHint.Range, "1.0,2000.0,1.0,or_greater")]
    public float FootstepBaseRadius { get; set; } = 130.0f;

    [Export(PropertyHint.Range, "1.0,2000.0,1.0,or_greater")]
    public float InteractionBaseRadius { get; set; } = 180.0f;

    [Export(PropertyHint.Range, "1.0,3000.0,1.0,or_greater")]
    public float GunshotBaseRadius { get; set; } = 650.0f;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float WallAttenuation { get; set; } = 0.5f;

    [Export(PropertyHint.Layers2DPhysics)]
    public uint OcclusionCollisionMask { get; set; } = CollisionLayers2D.World;

    public int ListenerCount => _listeners.Count;

    public event Action<NoiseOccurrence2D>? NoiseEmitted;

    public event Action<INoiseListener2D, PerceivedNoise2D>? NoiseDelivered;

    public override void _Ready()
    {
        ValidatePositiveFinite(FootstepBaseRadius, nameof(FootstepBaseRadius));
        ValidatePositiveFinite(InteractionBaseRadius, nameof(InteractionBaseRadius));
        ValidatePositiveFinite(GunshotBaseRadius, nameof(GunshotBaseRadius));
        if (!float.IsFinite(WallAttenuation) ||
            WallAttenuation <= 0.0f ||
            WallAttenuation > 1.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(NoiseSystem2D)} on '{Name}' requires wall attenuation within (0, 1].");
        }

        if ((OcclusionCollisionMask & CollisionLayers2D.World) == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(NoiseSystem2D)} on '{Name}' must test the World collision layer.");
        }
    }

    public override void _ExitTree()
    {
        _listeners.Clear();
        _emissionsThisProcessFrame.Clear();
        _occlusionQueryExclusions.Clear();
        _occlusionBarriers.Clear();
        NoiseEmitted = null;
        NoiseDelivered = null;
    }

    public void RegisterListener(INoiseListener2D listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        Node2D listenerNode = listener.ListenerNode2D;
        if (!GodotObject.IsInstanceValid(listenerNode) || !listenerNode.IsInsideTree())
        {
            throw new ArgumentException(
                "Noise listeners must be active 2D scene nodes.",
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

    public void UnregisterListener(INoiseListener2D listener)
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

    public NoiseOccurrence2D EmitNoise(
        Node source,
        NoiseKind kind,
        float intensity,
        Vector2 worldPosition,
        CollisionObject2D? originCollider = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidatePosition(worldPosition);
        if (originCollider is not null &&
            (!GodotObject.IsInstanceValid(originCollider) || !originCollider.IsInsideTree()))
        {
            throw new ArgumentException(
                "A supplied acoustic origin collider must be active.",
                nameof(originCollider));
        }

        NoiseEvent candidateNoise = new(
            source,
            kind,
            intensity,
            _nextSequenceId,
            Time.GetTicksMsec() / 1000.0,
            description);
        ulong processFrame = Engine.GetProcessFrames();
        BeginEmissionFrame(processFrame);
        NoiseOccurrence2D? duplicate = FindDuplicateEmission(
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
            throw new InvalidOperationException("The noise sequence identifier was exhausted.");
        }

        _nextSequenceId++;
        NoiseOccurrence2D occurrence = new(candidateNoise, worldPosition);
        RememberEmission(occurrence, originCollider);
        NoiseEmitted?.Invoke(occurrence);
        DeliverNoise(occurrence, originCollider);
        return occurrence;
    }

    private void DeliverNoise(
        NoiseOccurrence2D occurrence,
        CollisionObject2D? originCollider)
    {
        float baseRadius = GetBaseRadius(occurrence.Noise.Kind);
        for (int index = _listeners.Count - 1; index >= 0; index--)
        {
            INoiseListener2D listener = _listeners[index];
            Node2D listenerNode = listener.ListenerNode2D;
            if (!GodotObject.IsInstanceValid(listenerNode) || !listenerNode.IsInsideTree())
            {
                _listeners.RemoveAt(index);
                continue;
            }

            ValidateListenerTuning(listener);
            if (!listener.CanReceiveNoise || IsOwnNoise(listenerNode, occurrence.Noise.Source))
            {
                continue;
            }

            float distance = listenerNode.GlobalPosition.DistanceTo(occurrence.WorldPosition);
            float unoccludedRadius =
                baseRadius * occurrence.Noise.Intensity * listener.HearingSensitivity;
            if (distance > unoccludedRadius)
            {
                continue;
            }

            int barrierCount = CountOccludingBarriers(
                occurrence,
                listenerNode,
                originCollider);
            bool wasOccluded = barrierCount > 0;
            float perceivedIntensity = occurrence.Noise.Intensity *
                                       MathF.Pow(WallAttenuation, barrierCount);
            float effectiveRadius =
                baseRadius * perceivedIntensity * listener.HearingSensitivity;
            if (perceivedIntensity < listener.MinimumAudibleIntensity ||
                distance > effectiveRadius)
            {
                continue;
            }

            PerceivedNoise2D perceived = new(
                occurrence,
                perceivedIntensity,
                distance,
                effectiveRadius,
                wasOccluded);
            listener.ReceiveNoise(perceived);
            NoiseDelivered?.Invoke(listener, perceived);
        }
    }

    private int CountOccludingBarriers(
        NoiseOccurrence2D occurrence,
        Node2D listenerNode,
        CollisionObject2D? originCollider)
    {
        Vector2 listenerPosition = listenerNode.GlobalPosition;
        if (occurrence.WorldPosition.DistanceSquaredTo(listenerPosition) <= 0.0001f)
        {
            return 0;
        }

        _occlusionQueryExclusions.Clear();
        _occlusionBarriers.Clear();
        if (originCollider is not null)
        {
            AddOcclusionExclusion(originCollider.GetRid());
        }

        if (occurrence.Noise.Source is CollisionObject2D sourceCollider)
        {
            AddOcclusionExclusion(sourceCollider.GetRid());
        }

        if (listenerNode is CollisionObject2D listenerCollider)
        {
            AddOcclusionExclusion(listenerCollider.GetRid());
        }

        _occlusionSegment.A = occurrence.WorldPosition;
        _occlusionSegment.B = listenerPosition;
        PhysicsShapeQueryParameters2D query = new()
        {
            Shape = _occlusionSegment,
            Transform = Transform2D.Identity,
            CollisionMask = OcclusionCollisionMask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = _occlusionQueryExclusions
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> intersections =
            GetWorld2D().DirectSpaceState.IntersectShape(
                query,
                MaxOcclusionQueryResults);
        for (int index = 0;
             index < intersections.Count && _occlusionBarriers.Count < MaxOcclusionBarriers;
             index++)
        {
            Godot.Collections.Dictionary intersection = intersections[index];
            ulong colliderId = intersection["collider_id"].AsUInt64();
            int shapeIndex = intersection["shape"].AsInt32();
            _occlusionBarriers.Add(new OcclusionBarrierKey(colliderId, shapeIndex));
        }

        return _occlusionBarriers.Count;
    }

    private void AddOcclusionExclusion(Rid rid)
    {
        if (!rid.IsValid || _occlusionQueryExclusions.Contains(rid))
        {
            return;
        }

        _occlusionQueryExclusions.Add(rid);
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

    private NoiseOccurrence2D? FindDuplicateEmission(
        Node source,
        NoiseKind kind,
        float intensity,
        Vector2 position,
        CollisionObject2D? originCollider,
        string? description)
    {
        ulong sourceId = source.GetInstanceId();
        ulong originColliderId = originCollider?.GetInstanceId() ?? 0;
        for (int index = 0; index < _emissionsThisProcessFrame.Count; index++)
        {
            EmissionRecord record = _emissionsThisProcessFrame[index];
            if (record.SourceId == sourceId &&
                record.Kind == kind &&
                Mathf.IsEqualApprox(record.Intensity, intensity) &&
                record.Position.IsEqualApprox(position) &&
                record.OriginColliderId == originColliderId &&
                string.Equals(record.Description, description, StringComparison.Ordinal))
            {
                return record.Occurrence;
            }
        }

        return null;
    }

    private void RememberEmission(
        NoiseOccurrence2D occurrence,
        CollisionObject2D? originCollider)
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
            listener.HearingSensitivity <= 0.0f)
        {
            throw new InvalidOperationException(
                "Noise listener hearing sensitivity must be finite and positive.");
        }

        if (!float.IsFinite(listener.MinimumAudibleIntensity) ||
            listener.MinimumAudibleIntensity <= 0.0f)
        {
            throw new InvalidOperationException(
                "Noise listener minimum audible intensity must be finite and positive.");
        }
    }

    private static void ValidatePosition(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "Noise positions must be finite.");
        }
    }

    private static void ValidatePositiveFinite(float value, string propertyName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new InvalidOperationException(
                $"Noise system property '{propertyName}' must be finite and positive.");
        }
    }

    private readonly record struct OcclusionBarrierKey(
        ulong ColliderId,
        int ShapeIndex);

    private readonly struct EmissionRecord
    {
        public EmissionRecord(
            NoiseOccurrence2D occurrence,
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

        public NoiseOccurrence2D Occurrence { get; }

        public ulong SourceId { get; }

        public NoiseKind Kind { get; }

        public float Intensity { get; }

        public Vector2 Position { get; }

        public ulong OriginColliderId { get; }

        public string? Description { get; }
    }
}
