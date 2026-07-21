using System;
using System.Collections.Generic;
using Godot;
using LineZero.Gameplay.Interaction;

namespace LineZero.World2D.Interaction;

public sealed partial class PlayerInteractor2D : Node2D
{
    private const float MinimumDirectionLengthSquared = 0.0001f;
    private const float ScoreEqualityTolerance = 0.0001f;

    private readonly List<Interactable2D> _candidates = new();
    private readonly Godot.Collections.Array<Rid> _rayExclusions = new();

    private CollisionObject2D _actor = null!;
    private InteractionContext _context = null!;
    private Node2D _aimPivot = null!;
    private Area2D _detectionArea = null!;
    private Interactable2D? _currentTarget;
    private bool _isGameplayInputEnabled = true;

    [Export(PropertyHint.Range, "20.0,400.0,1.0,or_greater")]
    public float InteractionRange { get; set; } = 125.0f;

    [Export(PropertyHint.Range, "0.0,0.5,0.01")]
    public float SwitchThreshold { get; set; } = 0.08f;

    public string? CurrentPrompt { get; private set; }

    public bool IsGameplayInputEnabled => _isGameplayInputEnabled;

    public event Action<string?>? PromptChanged;

    public event Action<string>? MessageRequested;

    public event Action<IInteractable, InteractionContext, InteractionResult>?
        InteractionCompleted;

    public override void _Ready()
    {
        if (InteractionRange <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerInteractor2D)} on '{Name}' requires a positive range.");
        }

        if (SwitchThreshold < 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerInteractor2D)} on '{Name}' requires a non-negative threshold.");
        }

        _actor = GetParent() as CollisionObject2D
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerInteractor2D)} on '{Name}' must be a child of a CollisionObject2D.");

        _context = new InteractionContext(_actor);
        _aimPivot = GetNodeOrNull<Node2D>("%AimPivot")
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerInteractor2D)} on '{Name}' requires an AimPivot node.");

        _detectionArea = GetNodeOrNull<Area2D>("%DetectionArea")
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerInteractor2D)} on '{Name}' requires a DetectionArea node.");

        CollisionShape2D detectionShape = GetNodeOrNull<CollisionShape2D>(
            "%DetectionShape")
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerInteractor2D)} on '{Name}' requires a DetectionShape node.");

        if (detectionShape.Shape is not CircleShape2D circleShape)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerInteractor2D)} on '{Name}' requires a CircleShape2D.");
        }

        if (_detectionArea.CollisionLayer != 0 ||
            _detectionArea.CollisionMask != CollisionLayers2D.Interaction ||
            !_detectionArea.Monitoring)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerInteractor2D)} on '{Name}' has invalid collision settings.");
        }

        circleShape.Radius = InteractionRange;
        _rayExclusions.Add(_actor.GetRid());

        _detectionArea.AreaEntered += OnAreaEntered;
        _detectionArea.AreaExited += OnAreaExited;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_detectionArea))
        {
            _detectionArea.AreaEntered -= OnAreaEntered;
            _detectionArea.AreaExited -= OnAreaExited;
        }

        _candidates.Clear();
        _currentTarget = null;
        CurrentPrompt = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        RecomputeTarget();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isGameplayInputEnabled || !@event.IsActionPressed("interact"))
        {
            return;
        }

        TryInteract();
        GetViewport().SetInputAsHandled();
    }

    public void SetGameplayInputEnabled(bool enabled)
    {
        if (_isGameplayInputEnabled == enabled)
        {
            return;
        }

        _isGameplayInputEnabled = enabled;
        if (!enabled)
        {
            SetCurrentTarget(null);
            return;
        }

        RecomputeTarget();
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area is not Interactable2D interactable || ContainsCandidate(interactable))
        {
            return;
        }

        _candidates.Add(interactable);
        RecomputeTarget();
    }

    private void OnAreaExited(Area2D area)
    {
        if (area is not Interactable2D interactable)
        {
            return;
        }

        _candidates.Remove(interactable);
        if (ReferenceEquals(_currentTarget, interactable))
        {
            SetCurrentTarget(null);
        }

        RecomputeTarget();
    }

    private void TryInteract()
    {
        RecomputeTarget();

        Interactable2D? target = _currentTarget;
        if (target is null ||
            !GodotObject.IsInstanceValid(target) ||
            !target.CanInteract(_context))
        {
            SetCurrentTarget(null);
            return;
        }

        InteractionResult result = target.Interact(_context);
        InteractionCompleted?.Invoke(target, _context, result);

        string? message = result.Message;
        if (!string.IsNullOrWhiteSpace(message))
        {
            MessageRequested?.Invoke(message);
        }

        RecomputeTarget();
    }

    private void RecomputeTarget()
    {
        PruneInvalidCandidates();

        if (!_isGameplayInputEnabled)
        {
            SetCurrentTarget(null);
            return;
        }

        Interactable2D? bestTarget = null;
        float bestScore = float.NegativeInfinity;
        ulong bestInstanceId = ulong.MaxValue;

        for (int index = 0; index < _candidates.Count; index++)
        {
            Interactable2D candidate = _candidates[index];
            if (!TryScoreCandidate(candidate, out float candidateScore))
            {
                continue;
            }

            ulong candidateInstanceId = candidate.GetInstanceId();
            bool hasBetterScore = candidateScore > bestScore + ScoreEqualityTolerance;
            bool winsTie = MathF.Abs(candidateScore - bestScore) <= ScoreEqualityTolerance &&
                           candidateInstanceId < bestInstanceId;

            if (!hasBetterScore && !winsTie)
            {
                continue;
            }

            bestTarget = candidate;
            bestScore = candidateScore;
            bestInstanceId = candidateInstanceId;
        }

        if (_currentTarget is not null &&
            !ReferenceEquals(bestTarget, _currentTarget) &&
            ContainsCandidate(_currentTarget) &&
            TryScoreCandidate(_currentTarget, out float currentScore) &&
            bestTarget is not null &&
            !InteractionCandidateScorer.IsClearlyBetter(
                currentScore,
                bestScore,
                SwitchThreshold))
        {
            bestTarget = _currentTarget;
        }

        SetCurrentTarget(bestTarget);
    }

    private bool TryScoreCandidate(Interactable2D candidate, out float score)
    {
        score = float.NegativeInfinity;

        if (!GodotObject.IsInstanceValid(candidate) ||
            !candidate.IsInsideTree() ||
            !candidate.CanInteract(_context))
        {
            return false;
        }

        Vector2 targetOffset = candidate.InteractionPosition - GlobalPosition;
        float distance = targetOffset.Length();
        if (distance > InteractionRange)
        {
            return false;
        }

        if (!HasLineOfSight(candidate))
        {
            return false;
        }

        Vector2 facingDirection = Vector2.Right.Rotated(_aimPivot.GlobalRotation);
        float alignment = 1.0f;
        if (targetOffset.LengthSquared() > MinimumDirectionLengthSquared)
        {
            alignment = facingDirection.Dot(targetOffset / distance);
        }

        score = InteractionCandidateScorer.Calculate(
            distance,
            InteractionRange,
            alignment,
            candidate.InteractionPriority);

        return true;
    }

    private bool HasLineOfSight(Interactable2D candidate)
    {
        PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(
            GlobalPosition,
            candidate.InteractionPosition,
            CollisionLayers2D.World);

        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.Exclude = _rayExclusions;

        Godot.Collections.Dictionary hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return true;
        }

        GodotObject? collider = hit["collider"].AsGodotObject();
        return collider is Node colliderNode &&
               (ReferenceEquals(colliderNode, candidate) || candidate.IsAncestorOf(colliderNode));
    }

    private void PruneInvalidCandidates()
    {
        for (int index = _candidates.Count - 1; index >= 0; index--)
        {
            Interactable2D candidate = _candidates[index];
            if (GodotObject.IsInstanceValid(candidate) && candidate.IsInsideTree())
            {
                continue;
            }

            if (ReferenceEquals(_currentTarget, candidate))
            {
                SetCurrentTarget(null);
            }

            _candidates.RemoveAt(index);
        }
    }

    private bool ContainsCandidate(Interactable2D candidate)
    {
        for (int index = 0; index < _candidates.Count; index++)
        {
            if (ReferenceEquals(_candidates[index], candidate))
            {
                return true;
            }
        }

        return false;
    }

    private void SetCurrentTarget(Interactable2D? target)
    {
        string? nextPrompt = target?.InteractionPrompt;
        bool targetChanged = !ReferenceEquals(_currentTarget, target);
        bool promptChanged = !string.Equals(
            CurrentPrompt,
            nextPrompt,
            StringComparison.Ordinal);

        _currentTarget = target;
        CurrentPrompt = nextPrompt;

        if (targetChanged || promptChanged)
        {
            PromptChanged?.Invoke(nextPrompt);
        }
    }
}
