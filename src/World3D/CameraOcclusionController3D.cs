using System;
using System.Collections.Generic;
using Godot;
using LineZero.Core.Events;

namespace LineZero.World3D;

public sealed partial class CameraOcclusionController3D : Node
{
    private const int MaximumSupportedHits = 16;
    private const int SilhouetteRayCount = 5;

    private readonly HashSet<CameraOccluder3D> _activeOccluders = new(16);
    private readonly HashSet<CameraOccluder3D> _occludersThisTick = new(16);
    private readonly List<CameraOccluder3D> _restoreBuffer = new(16);
    private readonly Dictionary<CameraOccluder3D, int> _clearQueryCounts = new(16);
    private readonly Godot.Collections.Array<Rid> _queryExclusions = new();
    private readonly PhysicsRayQueryParameters3D _rayQuery = new();

    private Camera3D? _camera;
    private PhysicsBody3D? _targetBody;
    private double _elapsedSeconds;

    [Export(PropertyHint.Range, "0.02,0.5,0.01")]
    public double QueryIntervalSeconds { get; set; } = 0.05;

    [Export(PropertyHint.Range, "1,16,1")]
    public int MaximumOccludersPerQuery { get; set; } = 8;

    [Export(PropertyHint.Range, "0.25,1.5,0.05")]
    public float SilhouetteHalfWidth { get; set; } = 0.65f;

    [Export(PropertyHint.Range, "0.1,2.5,0.05")]
    public float CentreRayHeight { get; set; } = 0.9f;

    [Export(PropertyHint.Range, "0.05,1.0,0.05")]
    public float LowerRayHeight { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "0.5,3.0,0.05")]
    public float UpperRayHeight { get; set; } = 1.55f;

    [Export(PropertyHint.Range, "1,5,1")]
    public int ClearQueriesBeforeRestore { get; set; } = 2;

    [Export(PropertyHint.Layers3DPhysics)]
    public uint OccluderCollisionMask { get; set; } =
        CollisionLayers3D.CameraOccluder;

    public int FadedOccluderCount => _activeOccluders.Count;

    public event Action<int>? FadedOccluderCountChanged;

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

        ValidateRange(
            SilhouetteHalfWidth,
            0.25f,
            1.5f,
            nameof(SilhouetteHalfWidth));
        ValidateRange(
            CentreRayHeight,
            0.1f,
            2.5f,
            nameof(CentreRayHeight));
        ValidateRange(
            LowerRayHeight,
            0.05f,
            1.0f,
            nameof(LowerRayHeight));
        ValidateRange(
            UpperRayHeight,
            0.5f,
            3.0f,
            nameof(UpperRayHeight));
        if (LowerRayHeight >= CentreRayHeight ||
            UpperRayHeight <= CentreRayHeight)
        {
            throw new InvalidOperationException(
                "Ray heights must be ordered lower, centre, then upper.");
        }

        if (ClearQueriesBeforeRestore < 1 || ClearQueriesBeforeRestore > 5)
        {
            throw new InvalidOperationException(
                $"{nameof(ClearQueriesBeforeRestore)} must be within 1..5.");
        }

        if (OccluderCollisionMask != CollisionLayers3D.CameraOccluder)
        {
            throw new InvalidOperationException(
                "Camera occlusion must use only the dedicated occluder layer.");
        }

        _rayQuery.CollisionMask = OccluderCollisionMask;
        _rayQuery.CollideWithAreas = false;
        _rayQuery.CollideWithBodies = true;
        _rayQuery.Exclude = _queryExclusions;
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

        int previousCount = _activeOccluders.Count;
        _occludersThisTick.Clear();
        PhysicsDirectSpaceState3D spaceState = target.GetWorld3D().DirectSpaceState;
        _rayQuery.From = camera.GlobalPosition;
        Vector3 cameraRight = camera.GlobalTransform.Basis.X;
        cameraRight.Y = 0.0f;
        cameraRight = cameraRight.LengthSquared() > 0.000001f
            ? cameraRight.Normalized()
            : Vector3.Right;

        for (int rayIndex = 0; rayIndex < SilhouetteRayCount; rayIndex++)
        {
            _rayQuery.To = GetSilhouettePoint(
                target.GlobalPosition,
                cameraRight,
                rayIndex);
            _queryExclusions.Clear();
            _queryExclusions.Add(target.GetRid());
            for (int hitIndex = 0;
                 hitIndex < MaximumOccludersPerQuery;
                 hitIndex++)
            {
                // Godot arrays use copy-on-write semantics across property
                // boundaries. Reassign after bounded mutations so a ray cannot
                // rediscover the same body. The query object remains cached.
                _rayQuery.Exclude = _queryExclusions;
                Godot.Collections.Dictionary result =
                    spaceState.IntersectRay(_rayQuery);
                if (result.Count == 0 ||
                    !result.TryGetValue(
                        "collider",
                        out Variant colliderVariant))
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
            }
        }

        foreach (CameraOccluder3D occluder in _occludersThisTick)
        {
            _clearQueryCounts[occluder] = 0;
            bool added = _activeOccluders.Add(occluder);
            if (added || !occluder.IsOccluded)
            {
                occluder.SetOccluded(true);
            }
        }

        _restoreBuffer.Clear();
        foreach (CameraOccluder3D occluder in _activeOccluders)
        {
            if (!GodotObject.IsInstanceValid(occluder))
            {
                _restoreBuffer.Add(occluder);
                continue;
            }

            if (_occludersThisTick.Contains(occluder))
            {
                continue;
            }

            if (!occluder.IsOccluded)
            {
                if (occluder.CurrentFadeAmount <= 0.001f)
                {
                    _restoreBuffer.Add(occluder);
                }

                continue;
            }

            _clearQueryCounts.TryGetValue(
                occluder,
                out int consecutiveClearQueries);
            consecutiveClearQueries++;
            if (consecutiveClearQueries >= ClearQueriesBeforeRestore)
            {
                occluder.SetOccluded(false);
                _clearQueryCounts.Remove(occluder);
            }
            else
            {
                _clearQueryCounts[occluder] = consecutiveClearQueries;
            }
        }

        for (int index = 0; index < _restoreBuffer.Count; index++)
        {
            CameraOccluder3D occluder = _restoreBuffer[index];
            _activeOccluders.Remove(occluder);
            _clearQueryCounts.Remove(occluder);
        }

        PublishFadedOccluderCountIfChanged(previousCount);
    }

    private void RestoreAll()
    {
        int previousCount = _activeOccluders.Count;
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
        _clearQueryCounts.Clear();
        _queryExclusions.Clear();
        PublishFadedOccluderCountIfChanged(previousCount);
    }

    private void PublishFadedOccluderCountIfChanged(int previousCount)
    {
        if (previousCount == _activeOccluders.Count)
        {
            return;
        }

        SafeEventPublisher.Publish(
            FadedOccluderCountChanged,
            _activeOccluders.Count,
            $"{nameof(CameraOcclusionController3D)}." +
            nameof(FadedOccluderCountChanged));
    }

    private Vector3 GetSilhouettePoint(
        Vector3 targetPosition,
        Vector3 cameraRight,
        int rayIndex)
    {
        return rayIndex switch
        {
            0 => targetPosition + (Vector3.Up * CentreRayHeight),
            1 => targetPosition + (Vector3.Up * UpperRayHeight),
            2 => targetPosition +
                 (Vector3.Up * CentreRayHeight) +
                 (cameraRight * SilhouetteHalfWidth),
            3 => targetPosition +
                 (Vector3.Up * CentreRayHeight) -
                 (cameraRight * SilhouetteHalfWidth),
            4 => targetPosition + (Vector3.Up * LowerRayHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(rayIndex))
        };
    }

    private static void ValidateRange(
        float value,
        float minimum,
        float maximum,
        string propertyName)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{propertyName} must be between {minimum} and {maximum}.");
        }
    }
}
