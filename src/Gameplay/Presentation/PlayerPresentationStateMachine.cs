using System;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Movement;

namespace LineZero.Gameplay.Presentation;

/// <summary>
/// Resolves completed gameplay state into presentation state. This type never
/// mutates gameplay models and has no dependency on the scene tree.
/// </summary>
public sealed class PlayerPresentationStateMachine
{
    private readonly double _firePresentationSeconds;
    private readonly double _hitReactionSeconds;

    private MovementMode _movementMode = MovementMode.Walk;
    private MovementMode _posture = MovementMode.Walk;
    private PlayerPresentationAction _transientAction;
    private double _transientSecondsRemaining;
    private bool _isMoving;
    private bool _isReloading;
    private bool _isPresentationEnabled = true;
    private bool _terminalLatched;
    private bool _deathLatched;

    public PlayerPresentationStateMachine(
        double firePresentationSeconds,
        double hitReactionSeconds)
    {
        ValidateDuration(firePresentationSeconds, nameof(firePresentationSeconds));
        ValidateDuration(hitReactionSeconds, nameof(hitReactionSeconds));
        _firePresentationSeconds = firePresentationSeconds;
        _hitReactionSeconds = hitReactionSeconds;
        CurrentState = PlayerPresentationState.Idle;
    }

    public PlayerPresentationState CurrentState { get; private set; }

    public PlayerPresentationAction ActiveAction
    {
        get
        {
            if (_transientAction == PlayerPresentationAction.HitReaction)
            {
                return PlayerPresentationAction.HitReaction;
            }

            if (_isReloading)
            {
                return PlayerPresentationAction.Reload;
            }

            return _transientAction;
        }
    }

    public PlayerPresentationProfile CurrentProfile => _posture switch
    {
        MovementMode.Crouch => PlayerPresentationProfile.Crouch,
        MovementMode.Crawl => PlayerPresentationProfile.Crawl,
        _ => PlayerPresentationProfile.Standing,
    };

    public bool IsTerminal => _terminalLatched;

    public bool IsDead => _deathLatched;

    public bool IsReloading => _isReloading;

    public int FirePresentationCount { get; private set; }

    public int ReloadPresentationCount { get; private set; }

    public int HitPresentationCount { get; private set; }

    public ulong StateVersion { get; private set; }

    public ulong ActionSequence { get; private set; }

    public void UpdateLocomotion(
        MovementMode movementMode,
        MovementMode posture,
        bool isMoving)
    {
        ValidateMovementMode(movementMode, nameof(movementMode));
        ValidatePosture(posture);
        _movementMode = movementMode;
        _posture = posture;
        _isMoving = isMoving;
        ResolveState();
    }

    public void SetPresentationAvailability(
        bool presentationEnabled,
        bool terminal,
        bool dead)
    {
        if (dead)
        {
            _deathLatched = true;
            _terminalLatched = true;
            ClearActions();
        }
        else if (terminal)
        {
            _terminalLatched = true;
            ClearActions();
        }

        _isPresentationEnabled =
            presentationEnabled && !_terminalLatched && !_deathLatched;
        ResolveState();
    }

    public bool ObserveCompletedShot()
    {
        if (!_isPresentationEnabled ||
            _terminalLatched ||
            _deathLatched ||
            _isReloading)
        {
            return false;
        }

        _transientAction = PlayerPresentationAction.Fire;
        _transientSecondsRemaining = _firePresentationSeconds;
        FirePresentationCount++;
        ActionSequence++;
        ResolveState();
        return true;
    }

    public bool ObserveReload(ReloadStatus status)
    {
        switch (status)
        {
            case ReloadStatus.Started:
                if (!_isPresentationEnabled ||
                    _terminalLatched ||
                    _deathLatched ||
                    _isReloading)
                {
                    return false;
                }

                _isReloading = true;
                _transientAction = PlayerPresentationAction.None;
                _transientSecondsRemaining = 0.0;
                ReloadPresentationCount++;
                ActionSequence++;
                ResolveState();
                return true;

            case ReloadStatus.Completed:
            case ReloadStatus.Canceled:
                if (!_isReloading)
                {
                    return false;
                }

                _isReloading = false;
                ActionSequence++;
                ResolveState();
                return true;

            default:
                return false;
        }
    }

    public bool ObserveCompletedDamage(bool changed, bool causedDeath)
    {
        if (!changed || _terminalLatched || _deathLatched)
        {
            return false;
        }

        if (causedDeath)
        {
            _deathLatched = true;
            _terminalLatched = true;
            _isPresentationEnabled = false;
            ClearActions();
            ActionSequence++;
            ResolveState();
            return true;
        }

        _transientAction = PlayerPresentationAction.HitReaction;
        _transientSecondsRemaining = _hitReactionSeconds;
        HitPresentationCount++;
        ActionSequence++;
        ResolveState();
        return true;
    }

    public void ObserveDeath()
    {
        if (_deathLatched)
        {
            return;
        }

        _deathLatched = true;
        _terminalLatched = true;
        _isPresentationEnabled = false;
        ClearActions();
        ActionSequence++;
        ResolveState();
    }

    public void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta <= 0.0 ||
            _transientAction == PlayerPresentationAction.None)
        {
            return;
        }

        _transientSecondsRemaining = Math.Max(
            0.0,
            _transientSecondsRemaining - delta);
        if (_transientSecondsRemaining > 0.0)
        {
            return;
        }

        _transientAction = PlayerPresentationAction.None;
        ActionSequence++;
        ResolveState();
    }

    private void ClearActions()
    {
        _isReloading = false;
        _transientAction = PlayerPresentationAction.None;
        _transientSecondsRemaining = 0.0;
    }

    private void ResolveState()
    {
        PlayerPresentationState nextState;
        if (_deathLatched)
        {
            nextState = PlayerPresentationState.Death;
        }
        else if (_terminalLatched || !_isPresentationEnabled)
        {
            nextState = PlayerPresentationState.Disabled;
        }
        else if (_transientAction == PlayerPresentationAction.HitReaction)
        {
            nextState = PlayerPresentationState.HitReaction;
        }
        else if (_isReloading)
        {
            nextState = PlayerPresentationState.Reload;
        }
        else if (_transientAction == PlayerPresentationAction.Fire)
        {
            nextState = PlayerPresentationState.Fire;
        }
        else
        {
            nextState = ResolveLocomotionState();
        }

        if (CurrentState == nextState)
        {
            return;
        }

        CurrentState = nextState;
        StateVersion++;
    }

    private PlayerPresentationState ResolveLocomotionState()
    {
        return _posture switch
        {
            MovementMode.Crouch => _isMoving
                ? PlayerPresentationState.CrouchWalk
                : PlayerPresentationState.CrouchIdle,
            MovementMode.Crawl => _isMoving
                ? PlayerPresentationState.CrawlMove
                : PlayerPresentationState.CrawlIdle,
            _ when _movementMode == MovementMode.Sprint && _isMoving =>
                PlayerPresentationState.Sprint,
            _ when _isMoving => PlayerPresentationState.Walk,
            _ => PlayerPresentationState.Idle,
        };
    }

    private static void ValidateDuration(double value, string propertyName)
    {
        if (!double.IsFinite(value) || value <= 0.0 || value > 5.0)
        {
            throw new ArgumentOutOfRangeException(
                propertyName,
                "Presentation duration must be finite and between zero and five seconds.");
        }
    }

    private static void ValidatePosture(MovementMode posture)
    {
        ValidateMovementMode(posture, nameof(posture));
        if (posture == MovementMode.Sprint)
        {
            throw new ArgumentOutOfRangeException(
                nameof(posture),
                "Sprint is an effective movement mode, not a posture profile.");
        }
    }

    private static void ValidateMovementMode(
        MovementMode movementMode,
        string propertyName)
    {
        if (!Enum.IsDefined(movementMode))
        {
            throw new ArgumentOutOfRangeException(propertyName);
        }
    }
}
