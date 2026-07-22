using System;
using System.Collections.Generic;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Interaction;

namespace LineZero.World3D.Interaction;

public sealed partial class PlayerInteractor3D : Area3D
{
    private const float MinimumDirectionLengthSquared = 0.0001f;
    private const float ScoreEqualityTolerance = 0.0001f;
    private const int MaximumCandidates = 32;

    private readonly List<Interactable3D> _candidates = new();
    private readonly Godot.Collections.Array<Rid> _rayExclusions = new();

    private PlayerController3D? _actor;
    private PlayerAimController3D? _aimController;
    private InteractionContext? _context;
    private Interactable3D? _currentTarget;
    private double _selectionElapsedSeconds;
    private bool _isGameplayInputEnabled = true;

    [Export(PropertyHint.Range, "0.5,8.0,0.1")]
    public float InteractionRange { get; set; } = 3.2f;

    [Export(PropertyHint.Range, "0.0,0.5,0.01")]
    public float SwitchThreshold { get; set; } = 0.08f;

    [Export(PropertyHint.Range, "0.02,0.25,0.01")]
    public double SelectionIntervalSeconds { get; set; } = 0.05;

    [Export(PropertyHint.Layers3DPhysics)]
    public uint LineOfSightCollisionMask { get; set; } = CollisionLayers3D.World;

    public string? CurrentPrompt { get; private set; }

    public Interactable3D? CurrentTarget => _currentTarget;

    public bool IsGameplayInputEnabled => _isGameplayInputEnabled;

    public event Action<string?>? PromptChanged;

    public event Action<string>? MessageRequested;

    public event Action<IInteractable, InteractionContext, InteractionResult>?
        InteractionCompleted;

    public override void _Ready()
    {
        if (!float.IsFinite(InteractionRange) || InteractionRange <= 0.0f ||
            !float.IsFinite(SwitchThreshold) || SwitchThreshold < 0.0f ||
            !double.IsFinite(SelectionIntervalSeconds) ||
            SelectionIntervalSeconds < 0.02 ||
            SelectionIntervalSeconds > 0.25)
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerInteractor3D)} on '{Name}' has invalid tuning.");
        }

        CollisionShape3D sensorShape =
            GetNodeOrNull<CollisionShape3D>("%PlayerInteractionSensorShape3D")
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerInteractor3D)} on '{Name}' requires a sensor shape.");
        if (sensorShape.Shape is not SphereShape3D sphere ||
            sensorShape.Disabled ||
            sphere.Radius + 0.001f < InteractionRange)
        {
            throw new InvalidOperationException(
                "PlayerInteractionSensor3D requires an enabled constant sphere " +
                "covering the configured interaction range.");
        }

        if (CollisionLayer != CollisionLayers3D.PlayerInteractionSensor ||
            CollisionMask != CollisionLayers3D.InteractionArea ||
            !Monitoring ||
            Monitorable)
        {
            throw new InvalidOperationException(
                "PlayerInteractionSensor3D has invalid dedicated collision settings.");
        }

        if (LineOfSightCollisionMask != CollisionLayers3D.World)
        {
            throw new InvalidOperationException(
                "3D interaction line of sight must use only world geometry.");
        }

        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
        SetPhysicsProcess(false);
    }

    public override void _ExitTree()
    {
        AreaEntered -= OnAreaEntered;
        AreaExited -= OnAreaExited;
        _candidates.Clear();
        _rayExclusions.Clear();
        _currentTarget = null;
        _actor = null;
        _aimController = null;
        _context = null;
        CurrentPrompt = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        _selectionElapsedSeconds += delta;
        if (_selectionElapsedSeconds < SelectionIntervalSeconds)
        {
            return;
        }

        _selectionElapsedSeconds -= SelectionIntervalSeconds;
        if (_selectionElapsedSeconds > SelectionIntervalSeconds * 2.0)
        {
            // Selection is presentation feedback. At most one bounded refresh is
            // performed after a long frame; excess selection debt is clamped.
            _selectionElapsedSeconds = SelectionIntervalSeconds * 2.0;
        }

        RecomputeTarget();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Echo: true } ||
            !_isGameplayInputEnabled ||
            !@event.IsActionPressed("interact"))
        {
            return;
        }

        TryInteractCurrent();
        GetViewport().SetInputAsHandled();
    }

    public void Bind(
        PlayerController3D actor,
        PlayerAimController3D aimController)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(aimController);
        if (_actor is not null &&
            (!ReferenceEquals(_actor, actor) ||
             !ReferenceEquals(_aimController, aimController)))
        {
            throw new InvalidOperationException(
                $"{nameof(PlayerInteractor3D)} on '{Name}' is already bound.");
        }

        _actor = actor;
        _aimController = aimController;
        _context = new InteractionContext(actor);
        _rayExclusions.Clear();
        _rayExclusions.Add(actor.GetRid());
        _selectionElapsedSeconds = SelectionIntervalSeconds;
        SetPhysicsProcess(true);
    }

    public void SetGameplayInputEnabled(bool enabled)
    {
        PlayerController3D? actor = _actor;
        _isGameplayInputEnabled = enabled &&
                                  actor is not null &&
                                  !actor.IsTerminalState;
        if (!_isGameplayInputEnabled)
        {
            SetCurrentTarget(null);
        }
        else
        {
            RecomputeTarget();
        }
    }

    public InteractionResult TryInteractCurrent()
    {
        InteractionContext context = _context
            ?? throw new InvalidOperationException("3D interactor is not bound.");
        if (!_isGameplayInputEnabled)
        {
            return InteractionResult.None;
        }

        RecomputeTarget();
        Interactable3D? target = _currentTarget;
        if (target is null ||
            !GodotObject.IsInstanceValid(target) ||
            !target.CanInteract(context))
        {
            SetCurrentTarget(null);
            return InteractionResult.None;
        }

        InteractionResult result = target.Interact(context);
        SafeEventPublisher.Publish(
            InteractionCompleted,
            target,
            context,
            result,
            $"{nameof(PlayerInteractor3D)}.{nameof(InteractionCompleted)}");
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            SafeEventPublisher.Publish(
                MessageRequested,
                result.Message,
                $"{nameof(PlayerInteractor3D)}.{nameof(MessageRequested)}");
        }

        RecomputeTarget();
        return result;
    }

    private void OnAreaEntered(Area3D area)
    {
        if (area is not Interactable3D interactable ||
            ContainsCandidate(interactable) ||
            _candidates.Count >= MaximumCandidates)
        {
            return;
        }

        _candidates.Add(interactable);
        RecomputeTarget();
    }

    private void OnAreaExited(Area3D area)
    {
        if (area is not Interactable3D interactable)
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

    private void RecomputeTarget()
    {
        PruneInvalidCandidates();
        InteractionContext? context = _context;
        if (!_isGameplayInputEnabled || context is null)
        {
            SetCurrentTarget(null);
            return;
        }

        Interactable3D? bestTarget = null;
        float bestScore = float.NegativeInfinity;
        ulong bestInstanceId = ulong.MaxValue;
        for (int index = 0; index < _candidates.Count; index++)
        {
            Interactable3D candidate = _candidates[index];
            if (!TryScoreCandidate(candidate, context, out float candidateScore))
            {
                continue;
            }

            ulong candidateId = candidate.GetInstanceId();
            bool hasBetterScore =
                candidateScore > bestScore + ScoreEqualityTolerance;
            bool winsTie =
                MathF.Abs(candidateScore - bestScore) <= ScoreEqualityTolerance &&
                candidateId < bestInstanceId;
            if (!hasBetterScore && !winsTie)
            {
                continue;
            }

            bestTarget = candidate;
            bestScore = candidateScore;
            bestInstanceId = candidateId;
        }

        if (_currentTarget is not null &&
            !ReferenceEquals(bestTarget, _currentTarget) &&
            bestTarget is not null &&
            ContainsCandidate(_currentTarget) &&
            TryScoreCandidate(_currentTarget, context, out float currentScore) &&
            !InteractionCandidateScorer.IsClearlyBetter(
                currentScore,
                bestScore,
                SwitchThreshold))
        {
            bestTarget = _currentTarget;
        }

        SetCurrentTarget(bestTarget);
    }

    private bool TryScoreCandidate(
        Interactable3D candidate,
        InteractionContext context,
        out float score)
    {
        score = float.NegativeInfinity;
        PlayerController3D actor = _actor
            ?? throw new InvalidOperationException("3D interactor actor is missing.");
        if (!GodotObject.IsInstanceValid(candidate) ||
            !candidate.IsInsideTree() ||
            !candidate.CanInteract(context))
        {
            return false;
        }

        Vector3 targetOffset = candidate.InteractionPosition - actor.GlobalPosition;
        targetOffset.Y = 0.0f;
        float distance = targetOffset.Length();
        if (distance > InteractionRange || !HasLineOfSight(candidate, actor))
        {
            return false;
        }

        Vector3 facingDirection = Vector3.Forward;
        _aimController?.TryGetAimDirection(out facingDirection);
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

    private bool HasLineOfSight(
        Interactable3D candidate,
        PlayerController3D actor)
    {
        _rayExclusions.Clear();
        _rayExclusions.Add(actor.GetRid());
        CollisionObject3D? interactionOccluder = candidate.InteractionOccluder;
        if (interactionOccluder is not null &&
            GodotObject.IsInstanceValid(interactionOccluder))
        {
            _rayExclusions.Add(interactionOccluder.GetRid());
        }

        PhysicsRayQueryParameters3D query =
            PhysicsRayQueryParameters3D.Create(
                actor.GlobalPosition + (Vector3.Up * 0.75f),
                candidate.InteractionPosition + (Vector3.Up * 0.5f),
                LineOfSightCollisionMask,
                _rayExclusions);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.HitFromInside = true;
        return actor.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    private void PruneInvalidCandidates()
    {
        for (int index = _candidates.Count - 1; index >= 0; index--)
        {
            Interactable3D candidate = _candidates[index];
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

    private bool ContainsCandidate(Interactable3D candidate)
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

    private void SetCurrentTarget(Interactable3D? target)
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
            SafeEventPublisher.Publish(
                PromptChanged,
                nextPrompt,
                $"{nameof(PlayerInteractor3D)}.{nameof(PromptChanged)}");
        }
    }
}
