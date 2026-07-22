using System;
using System.Collections.Generic;
using Godot;

namespace LineZero.World3D;

public sealed partial class CameraOcclusionController3D : Node
{
    private const int MaximumSupportedHits = 16;

    private readonly HashSet<CameraOccluder3D> _activeOccluders = new();
    private readonly HashSet<CameraOccluder3D> _occludersThisTick = new();
    private readonly List<CameraOccluder3D> _restoreBuffer = new();
    private readonly Godot.Collections.Array<Rid> _queryExclusions = new();

    private Camera3D? _camera;
    private PhysicsBody3D? _targetBody;
    private double _elapsedSeconds;

    [Export(PropertyHint.Range, "0.02,0.5,0.01")]
    public double QueryIntervalSeconds { get; set; } = 0.05;

    [Export(PropertyHint.Range, "1,16,1")]
    public int MaximumOccludersPerQuery { get; set; } = 8;

    [Export(PropertyHint.Layers3DPhysics)]
    public uint OccluderCollisionMask { get; set; } =
        CollisionLayers3D.CameraOccluder;

    public override void _Ready()
    {
        if (!double.IsFinite(QueryIntervalSeconds) ||
            QueryIntervalSeconds < 0.02 ||
            QueryIntervalSeconds > 0.5)
        {
            throw new InvalidOperationException(
                $"{nameof(QueryIntervalSeconds)} must be between 0.02 and 0.5.");
        }

        if (MaximumOccludersPerQuery < 1 ||
            MaximumOccludersPerQuery > MaximumSupportedHits)
        {
            throw new InvalidOperationException(
                $"{nameof(MaximumOccludersPerQuery)} must be within 1..{MaximumSupportedHits}.");
        }

        if (OccluderCollisionMask != CollisionLayers3D.CameraOccluder)
        {
            throw new InvalidOperationException(
                "Camera occlusion must use only the dedicated occluder layer.");
        }

        SetPhysicsProcess(false);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        _elapsedSeconds += delta;
        if (_elapsedSeconds < QueryIntervalSeconds)
        {
            return;
        }

        _elapsedSeconds -= QueryIntervalSeconds;
        if (_elapsedSeconds > QueryIntervalSeconds * 2.0)
        {
            // Occlusion is presentation-only. Excess debt is clamped so a long
            // frame cannot trigger an unbounded burst of physics queries.
            _elapsedSeconds = QueryIntervalSeconds * 2.0;
        }

        RefreshOcclusion();
    }

    public override void _ExitTree()
    {
        RestoreAll();
        _camera = null;
        _targetBody = null;
    }

    public void Bind(Camera3D camera, PhysicsBody3D targetBody)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(targetBody);
        if (_camera is not null &&
            (!ReferenceEquals(_camera, camera) ||
             !ReferenceEquals(_targetBody, targetBody)))
        {
            throw new InvalidOperationException(
                $"{nameof(CameraOcclusionController3D)} on '{Name}' is already bound.");
        }

        _camera = camera;
        _targetBody = targetBody;
        _elapsedSeconds = QueryIntervalSeconds;
        SetPhysicsProcess(true);
    }

    public void RefreshOcclusion()
    {
        Camera3D camera = _camera
            ?? throw new InvalidOperationException("Camera occlusion camera is missing.");
        PhysicsBody3D target = _targetBody
            ?? throw new InvalidOperationException("Camera occlusion target is missing.");

        _occludersThisTick.Clear();
        _queryExclusions.Clear();
        _queryExclusions.Add(target.GetRid());
        Vector3 rayEnd = target.GlobalPosition + (Vector3.Up * 0.9f);
        PhysicsDirectSpaceState3D spaceState = target.GetWorld3D().DirectSpaceState;

        for (int hitIndex = 0;
             hitIndex < MaximumOccludersPerQuery;
             hitIndex++)
        {
            PhysicsRayQueryParameters3D query =
                PhysicsRayQueryParameters3D.Create(
                    camera.GlobalPosition,
                    rayEnd,
                    OccluderCollisionMask,
                    _queryExclusions);
            query.CollideWithAreas = false;
            query.CollideWithBodies = true;
            Godot.Collections.Dictionary result = spaceState.IntersectRay(query);
            if (result.Count == 0 ||
                !result.TryGetValue("collider", out Variant colliderVariant))
            {
                break;
            }

            GodotObject? collider = colliderVariant.AsGodotObject();
            if (collider is not CameraOccluder3D occluder)
            {
                break;
            }

            _queryExclusions.Add(occluder.GetRid());
            _occludersThisTick.Add(occluder);
            if (_activeOccluders.Add(occluder))
            {
                occluder.SetOccluded(true);
            }
        }

        _restoreBuffer.Clear();
        foreach (CameraOccluder3D occluder in _activeOccluders)
        {
            if (!_occludersThisTick.Contains(occluder))
            {
                _restoreBuffer.Add(occluder);
            }
        }

        for (int index = 0; index < _restoreBuffer.Count; index++)
        {
            CameraOccluder3D occluder = _restoreBuffer[index];
            if (GodotObject.IsInstanceValid(occluder))
            {
                occluder.SetOccluded(false);
            }

            _activeOccluders.Remove(occluder);
        }
    }

    private void RestoreAll()
    {
        foreach (CameraOccluder3D occluder in _activeOccluders)
        {
            if (GodotObject.IsInstanceValid(occluder))
            {
                occluder.SetOccluded(false);
            }
        }

        _activeOccluders.Clear();
        _occludersThisTick.Clear();
        _restoreBuffer.Clear();
        _queryExclusions.Clear();
    }
}
