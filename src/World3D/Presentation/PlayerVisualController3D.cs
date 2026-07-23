using System;
using Godot;
using LineZero.Core.Events;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Presentation;
using LineZero.World3D.Combat;
using LineZero.World3D.Flashlight;

namespace LineZero.World3D.Presentation;

public sealed partial class PlayerVisualController3D : Node3D
{
    private const int PlayableStateCount =
        (int)PlayerPresentationState.Death + 1;
    private const int MaximumDebugCatchUpTicks = 2;

    private static readonly StringName AnimationPlaybackParameter =
        new("parameters/playback");
    private static readonly StringName IdleTreeState = new("Idle");
    private static readonly StringName WalkTreeState = new("Walk");
    private static readonly StringName SprintTreeState = new("Sprint");
    private static readonly StringName CrouchIdleTreeState = new("CrouchIdle");
    private static readonly StringName CrouchWalkTreeState = new("CrouchWalk");
    private static readonly StringName CrawlIdleTreeState = new("CrawlIdle");
    private static readonly StringName CrawlMoveTreeState = new("CrawlMove");
    private static readonly StringName FireTreeState = new("Fire");
    private static readonly StringName ReloadTreeState = new("Reload");
    private static readonly StringName HitReactionTreeState = new("HitReaction");
    private static readonly StringName DeathTreeState = new("Death");

    private readonly bool[] _clipAvailable = new bool[PlayableStateCount];

    private Node3D _modelYawRoot = null!;
    private Node3D _modelAlignmentRoot = null!;
    private Node3D _importedModelRoot = null!;
    private Node3D _developmentFallbackRoot = null!;
    private Node3D _fallbackMotionRoot = null!;
    private Node3D _fallbackFigureRoot = null!;
    private Node3D _socketRoot = null!;
    private Node3D _weaponRecoilRoot = null!;
    private MeshInstance3D _muzzleFlash = null!;
    private AnimationPlayer _animationPlayer = null!;
    private AnimationTree _animationTree = null!;
    private AnimationNodeStateMachinePlayback? _animationPlayback;
    private PlayerPresentationStateMachine _presentation = null!;
    private PlayerController3D? _player;
    private PlayerAimController3D? _aimController;
    private PlayerWeaponController3D? _weaponController;
    private HealthModel? _health;
    private Node3D? _aimPivot;
    private StringName? _locomotionBlendParameter;
    private StringName? _animationSpeedParameter;
    private PlayerLocomotionBlendResult _locomotion;
    private PlayerVisualDebugSnapshot _lastDebugSnapshot;
    private Vector3 _weaponRecoilRestPosition;
    private float _visualYaw;
    private float _fallbackCycleRadians;
    private float _recoilStrength;
    private double _muzzleFlashSecondsRemaining;
    private double _debugElapsedSeconds;
    private ulong _lastAppliedStateVersion;
    private ulong _lastAppliedActionSequence;
    private ulong _lastPublishedStateVersion;
    private ulong _lastPublishedActionSequence;
    private bool _hasDebugSnapshot;
    private bool _hasAppliedState;
    private bool _isBound;

    [Export]
    public bool ForceDevelopmentFallback { get; set; }

    [Export]
    public bool EnableImportedAnimationTree { get; set; } = true;

    [Export]
    public PlayerAnimationSet3D? AnimationSet { get; set; }

    [Export]
    public string LocomotionBlendParameterPath { get; set; } = string.Empty;

    [Export]
    public string AnimationSpeedParameterPath { get; set; } = string.Empty;

    [Export(PropertyHint.Range, "0.0,2.0,0.01")]
    public float IdleStopSpeed { get; set; } = 0.05f;

    [Export(PropertyHint.Range, "0.01,3.0,0.01")]
    public float IdleStartSpeed { get; set; } = 0.12f;

    [Export(PropertyHint.Range, "1.0,40.0,0.5")]
    public float VisualYawResponse { get; set; } = 18.0f;

    [Export(PropertyHint.Range, "1.0,40.0,0.5")]
    public float PostureResponse { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "0.1,2.0,0.05")]
    public float MinimumAnimationSpeed { get; set; } = 0.55f;

    [Export(PropertyHint.Range, "0.1,3.0,0.05")]
    public float MaximumAnimationSpeed { get; set; } = 1.35f;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public double FirePresentationSeconds { get; set; } = 0.12;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public double HitReactionSeconds { get; set; } = 0.22;

    [Export(PropertyHint.Range, "0.01,0.5,0.01")]
    public double MuzzleFlashSeconds { get; set; } = 0.06;

    [Export(PropertyHint.Range, "0.0,0.3,0.005")]
    public float RecoilDistance { get; set; } = 0.08f;

    [Export(PropertyHint.Range, "1.0,50.0,0.5")]
    public float RecoilRecovery { get; set; } = 22.0f;

    [Export(PropertyHint.Range, "0.05,1.0,0.05")]
    public double DebugUpdateIntervalSeconds { get; set; } = 0.2;

    public bool HasImportedModel { get; private set; }

    public bool IsImportedModelValid { get; private set; }

    public bool IsUsingDevelopmentFallback { get; private set; }

    public bool HasValidSocketHierarchy { get; private set; }

    public bool IsAnimationTreeActive => _animationTree.Active;

    public int MissingClipCount { get; private set; }

    public PlayerPresentationState CurrentState => _presentation.CurrentState;

    public PlayerPresentationAction ActiveAction => _presentation.ActiveAction;

    public PlayerPresentationProfile CurrentProfile =>
        _presentation.CurrentProfile;

    public Vector2 LocalLocomotionBlend => _locomotion.LocalBlend;

    public float CurrentVisualYaw => _visualYaw;

    public float AnimationPlaybackSpeed { get; private set; } = 1.0f;

    public PlayerPresentationStateMachine Presentation => _presentation;

    public Node3D ImportedModelRoot => _importedModelRoot;

    public Node3D DevelopmentFallbackRoot => _developmentFallbackRoot;

    public Node3D ModelAlignmentRoot => _modelAlignmentRoot;

    public Marker3D RightHandSocket { get; private set; } = null!;

    public Marker3D WeaponSocket { get; private set; } = null!;

    public Marker3D WeaponOrigin { get; private set; } = null!;

    public Marker3D MuzzleSocket { get; private set; } = null!;

    public Marker3D FlashlightSocket { get; private set; } = null!;

    public Marker3D CameraTarget { get; private set; } = null!;

    public PlayerFlashlightController3D FlashlightController { get; private set; } = null!;

    public AnimationPlayer AnimationPlayer => _animationPlayer;

    public AnimationTree AnimationTree => _animationTree;

    public event Action? PresentationChanged;

    public event Action<PlayerVisualDebugSnapshot>? DebugSnapshotChanged;

    public override void _Ready()
    {
        ValidateConfiguration();
        CacheAuthoredNodes();
        ConfigureVisualSource();
        ConfigureAnimationDriver();
        _presentation = new PlayerPresentationStateMachine(
            FirePresentationSeconds,
            HitReactionSeconds);
        _weaponRecoilRestPosition = _weaponRecoilRoot.Position;
        _muzzleFlash.Visible = false;
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        Unbind();
        PresentationChanged = null;
        DebugSnapshotChanged = null;
    }

    public override void _Process(double delta)
    {
        PlayerController3D player = _player
            ?? throw new InvalidOperationException("Player visual binding is missing.");
        Node3D aimPivot = _aimPivot
            ?? throw new InvalidOperationException("Player aim pivot binding is missing.");
        float frameSeconds = float.IsFinite((float)delta) && delta > 0.0
            ? (float)delta
            : 0.0f;

        _presentation.SetPresentationAvailability(
            player.CanAcceptGameplayInput,
            player.IsTerminalState,
            player.Health.IsDead);
        Basis aimBasis = aimPivot.GlobalTransform.Basis;
        _locomotion = PlayerLocomotionBlend3D.Calculate(
            player.Velocity,
            -aimBasis.Z,
            aimBasis.X,
            player.GetMovementSpeed(player.CurrentMovementMode),
            IdleStopSpeed,
            IdleStartSpeed,
            _locomotion.IsMoving);
        _presentation.UpdateLocomotion(
            player.CurrentMovementMode,
            player.CurrentPosture,
            _locomotion.IsMoving);
        _presentation.Advance(delta);

        UpdateVisualYaw(aimPivot.GlobalRotation.Y, frameSeconds);
        UpdatePosturePresentation(frameSeconds);
        UpdateFallbackMotion(frameSeconds);
        UpdateShotEffects(frameSeconds);
        UpdateAnimationDriver();
        PublishChangesIfNeeded(delta);
    }

    public void Bind(
        PlayerController3D player,
        PlayerAimController3D aimController,
        PlayerWeaponController3D weaponController,
        HealthModel health,
        Node3D aimPivot)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(aimController);
        ArgumentNullException.ThrowIfNull(weaponController);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(aimPivot);
        if (_isBound)
        {
            if (ReferenceEquals(_player, player) &&
                ReferenceEquals(_aimController, aimController) &&
                ReferenceEquals(_weaponController, weaponController) &&
                ReferenceEquals(_health, health) &&
                ReferenceEquals(_aimPivot, aimPivot))
            {
                return;
            }

            throw new InvalidOperationException(
                $"{nameof(PlayerVisualController3D)} on '{Name}' is already bound.");
        }

        _player = player;
        _aimController = aimController;
        _weaponController = weaponController;
        _health = health;
        _aimPivot = aimPivot;
        _visualYaw = aimPivot.GlobalRotation.Y;
        player.MovementModeChanged += OnMovementModeChanged;
        player.PostureChanged += OnPostureChanged;
        player.InputStateChanged += OnInputStateChanged;
        weaponController.ShotResolved += OnShotResolved;
        weaponController.ReloadChanged += OnReloadChanged;
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        _isBound = true;
        SynchronizeAuthoritativeState();
        SetProcess(true);
        PublishDebugSnapshot(force: true);
    }

    private void CacheAuthoredNodes()
    {
        _modelYawRoot = RequireNode<Node3D>("ModelYawRoot3D");
        _modelAlignmentRoot = RequireNode<Node3D>(
            "ModelYawRoot3D/ModelAlignmentRoot3D");
        _importedModelRoot = RequireNode<Node3D>(
            "ModelYawRoot3D/ModelAlignmentRoot3D/ImportedModelRoot3D");
        _developmentFallbackRoot = RequireNode<Node3D>(
            "ModelYawRoot3D/ModelAlignmentRoot3D/DevelopmentFallbackRoot3D");
        _fallbackMotionRoot = RequireNode<Node3D>(
            "ModelYawRoot3D/ModelAlignmentRoot3D/DevelopmentFallbackRoot3D/FallbackMotionRoot3D");
        _fallbackFigureRoot = RequireNode<Node3D>(
            "ModelYawRoot3D/ModelAlignmentRoot3D/DevelopmentFallbackRoot3D/FallbackMotionRoot3D/FallbackFigureRoot3D");
        _socketRoot = RequireNode<Node3D>("PresentationSocketRoot3D");
        RightHandSocket = RequireNode<Marker3D>(
            "PresentationSocketRoot3D/RightHandSocket");
        WeaponSocket = RequireNode<Marker3D>(
            "PresentationSocketRoot3D/RightHandSocket/WeaponSocket");
        WeaponOrigin = RequireNode<Marker3D>(
            "PresentationSocketRoot3D/RightHandSocket/WeaponSocket/WeaponOrigin");
        MuzzleSocket = RequireNode<Marker3D>(
            "PresentationSocketRoot3D/RightHandSocket/WeaponSocket/MuzzleSocket");
        FlashlightSocket = RequireNode<Marker3D>(
            "PresentationSocketRoot3D/RightHandSocket/WeaponSocket/FlashlightSocket");
        _weaponRecoilRoot = RequireNode<Node3D>(
            "PresentationSocketRoot3D/RightHandSocket/WeaponSocket/WeaponRecoilRoot3D");
        _muzzleFlash = RequireNode<MeshInstance3D>(
            "PresentationSocketRoot3D/RightHandSocket/WeaponSocket/MuzzleSocket/MuzzleFlash3D");
        FlashlightController = RequireNode<PlayerFlashlightController3D>(
            "PresentationSocketRoot3D/RightHandSocket/WeaponSocket/FlashlightSocket/PlayerFlashlightController3D");
        CameraTarget = RequireNode<Marker3D>("CameraTarget");
        _animationPlayer = RequireNode<AnimationPlayer>("AnimationPlayer");
        _animationTree = RequireNode<AnimationTree>("AnimationTree");

        HasValidSocketHierarchy =
            ReferenceEquals(RightHandSocket.GetParent(), _socketRoot) &&
            ReferenceEquals(WeaponSocket.GetParent(), RightHandSocket) &&
            ReferenceEquals(WeaponOrigin.GetParent(), WeaponSocket) &&
            ReferenceEquals(MuzzleSocket.GetParent(), WeaponSocket) &&
            ReferenceEquals(FlashlightSocket.GetParent(), WeaponSocket) &&
            ReferenceEquals(FlashlightController.GetParent(), FlashlightSocket) &&
            ReferenceEquals(_muzzleFlash.GetParent(), MuzzleSocket) &&
            FlashlightSocket.Position.IsFinite() &&
            FlashlightSocket.Position.Z < -0.5f;
        if (!HasValidSocketHierarchy)
        {
            throw new InvalidOperationException(
                "PlayerVisual3D has an invalid authored socket hierarchy.");
        }
    }

    private void ConfigureVisualSource()
    {
        HasImportedModel = _importedModelRoot.GetChildCount() > 0;
        IsImportedModelValid = HasImportedModel &&
                               PrepareImportedPresentationTree(
                                   _importedModelRoot);
        IsUsingDevelopmentFallback =
            ForceDevelopmentFallback || !IsImportedModelValid;
        _importedModelRoot.Visible = !IsUsingDevelopmentFallback;
        _developmentFallbackRoot.Visible = IsUsingDevelopmentFallback;
        if (!HasImportedModel)
        {
            GD.Print(
                "[PlayerVisual3D] No imported player model is configured; " +
                "using the isolated development fallback.");
        }
        else if (!IsImportedModelValid)
        {
            GD.PushError(
                "[PlayerVisual3D] Imported player presentation contains physical " +
                "collision. The model was disabled and the development fallback " +
                "was kept active.");
        }
    }

    private static bool PrepareImportedPresentationTree(Node node)
    {
        bool valid = true;
        for (int index = 0; index < node.GetChildCount(); index++)
        {
            Node child = node.GetChild(index);
            if (child is CollisionObject3D or CollisionShape3D)
            {
                valid = false;
            }
            else if (child is Camera3D camera)
            {
                camera.Current = false;
            }
            else if (child is Light3D light)
            {
                light.Visible = false;
            }
            else if (child is GeometryInstance3D geometry)
            {
                geometry.Layers = RenderLayers3D.PlayerVisual;
            }

            if (!PrepareImportedPresentationTree(child))
            {
                valid = false;
            }
        }

        return valid;
    }

    private void ConfigureAnimationDriver()
    {
        _animationTree.Active = false;
        MissingClipCount = PlayableStateCount;
        if (AnimationSet is null ||
            !EnableImportedAnimationTree ||
            IsUsingDevelopmentFallback ||
            _animationTree.TreeRoot is not AnimationNodeStateMachine stateMachine)
        {
            return;
        }

        Variant playbackVariant = _animationTree.Get(AnimationPlaybackParameter);
        _animationPlayback = playbackVariant.AsGodotObject()
            as AnimationNodeStateMachinePlayback;
        if (_animationPlayback is null)
        {
            return;
        }

        MissingClipCount = 0;
        for (int index = 0; index < PlayableStateCount; index++)
        {
            PlayerPresentationState state = (PlayerPresentationState)index;
            string clipName = AnimationSet.GetClipName(state);
            StringName treeState = GetTreeStateName(state);
            bool available = !string.IsNullOrWhiteSpace(clipName) &&
                             _animationPlayer.HasAnimation(new StringName(clipName)) &&
                             stateMachine.HasNode(treeState);
            _clipAvailable[index] = available;
            if (!available)
            {
                MissingClipCount++;
            }
        }

        _locomotionBlendParameter = CacheParameterPath(
            LocomotionBlendParameterPath);
        _animationSpeedParameter = CacheParameterPath(
            AnimationSpeedParameterPath);
        _animationTree.Active = true;
    }

    private void SynchronizeAuthoritativeState()
    {
        PlayerController3D player = _player
            ?? throw new InvalidOperationException("Player binding is missing.");
        _presentation.SetPresentationAvailability(
            player.CanAcceptGameplayInput,
            player.IsTerminalState,
            player.Health.IsDead);
        _presentation.UpdateLocomotion(
            player.CurrentMovementMode,
            player.CurrentPosture,
            IsHorizontallyMoving(player.Velocity));
        ApplyAnimationState(force: true);
    }

    private void UpdateVisualYaw(float targetYaw, float delta)
    {
        if (!float.IsFinite(targetYaw) || delta <= 0.0f)
        {
            return;
        }

        float blend = ExponentialBlend(VisualYawResponse, delta);
        _visualYaw = Mathf.LerpAngle(_visualYaw, targetYaw, blend);
        float targetRoll = _presentation.CurrentState switch
        {
            PlayerPresentationState.Death => Mathf.DegToRad(84.0f),
            PlayerPresentationState.HitReaction => Mathf.DegToRad(6.0f),
            _ => 0.0f,
        };
        Vector3 rotation = _modelYawRoot.GlobalRotation;
        rotation.X = 0.0f;
        rotation.Y = _visualYaw;
        rotation.Z = Mathf.LerpAngle(rotation.Z, targetRoll, blend);
        _modelYawRoot.GlobalRotation = rotation;
    }

    private void UpdatePosturePresentation(float delta)
    {
        if (delta <= 0.0f)
        {
            return;
        }

        PlayerPresentationProfile profile = _presentation.CurrentProfile;
        float blend = ExponentialBlend(PostureResponse, delta);
        float socketHeight = profile switch
        {
            PlayerPresentationProfile.Crouch => -0.25f,
            PlayerPresentationProfile.Crawl => -0.55f,
            _ => 0.0f,
        };
        Vector3 socketPosition = _socketRoot.Position;
        socketPosition.Y = Mathf.Lerp(socketPosition.Y, socketHeight, blend);
        _socketRoot.Position = socketPosition;

        if (!IsUsingDevelopmentFallback)
        {
            return;
        }

        Vector3 fallbackScale = profile switch
        {
            PlayerPresentationProfile.Crouch => new Vector3(1.0f, 0.72f, 1.0f),
            PlayerPresentationProfile.Crawl => new Vector3(1.0f, 0.36f, 1.28f),
            _ => Vector3.One,
        };
        _fallbackFigureRoot.Scale = _fallbackFigureRoot.Scale.Lerp(
            fallbackScale,
            blend);
    }

    private void UpdateFallbackMotion(float delta)
    {
        if (!IsUsingDevelopmentFallback || delta <= 0.0f)
        {
            return;
        }

        bool locomoting = _locomotion.IsMoving &&
            _presentation.CurrentState is not (
                PlayerPresentationState.Death or
                PlayerPresentationState.Disabled or
                PlayerPresentationState.Reload);
        float cycleRate = _presentation.CurrentState == PlayerPresentationState.Sprint
            ? 13.0f
            : _presentation.CurrentProfile == PlayerPresentationProfile.Crawl
                ? 6.0f
                : 9.0f;
        if (locomoting)
        {
            _fallbackCycleRadians = Mathf.PosMod(
                _fallbackCycleRadians + (cycleRate * delta),
                Mathf.Tau);
        }

        float targetBob = locomoting
            ? Mathf.Sin(_fallbackCycleRadians) *
              (_presentation.CurrentState == PlayerPresentationState.Sprint
                  ? 0.045f
                  : 0.025f)
            : 0.0f;
        Vector3 motionPosition = _fallbackMotionRoot.Position;
        motionPosition.Y = Mathf.Lerp(
            motionPosition.Y,
            targetBob,
            ExponentialBlend(16.0f, delta));
        _fallbackMotionRoot.Position = motionPosition;
    }

    private void UpdateShotEffects(float delta)
    {
        if (_muzzleFlashSecondsRemaining > 0.0)
        {
            _muzzleFlashSecondsRemaining = Math.Max(
                0.0,
                _muzzleFlashSecondsRemaining - delta);
            _muzzleFlash.Visible = _muzzleFlashSecondsRemaining > 0.0;
        }

        if (delta <= 0.0f)
        {
            return;
        }

        _recoilStrength = Mathf.Lerp(
            _recoilStrength,
            0.0f,
            ExponentialBlend(RecoilRecovery, delta));
        _weaponRecoilRoot.Position = _weaponRecoilRestPosition +
            (Vector3.Back * (RecoilDistance * _recoilStrength));
    }

    private void UpdateAnimationDriver()
    {
        AnimationPlaybackSpeed = _locomotion.IsMoving
            ? Mathf.Clamp(
                _locomotion.SpeedRatio,
                MinimumAnimationSpeed,
                MaximumAnimationSpeed)
            : 1.0f;
        if (!_animationTree.Active)
        {
            return;
        }

        if (_locomotionBlendParameter is StringName blendParameter)
        {
            _animationTree.Set(blendParameter, _locomotion.LocalBlend);
        }

        if (_animationSpeedParameter is StringName speedParameter)
        {
            _animationTree.Set(speedParameter, AnimationPlaybackSpeed);
        }

        ApplyAnimationState(force: false);
    }

    private void ApplyAnimationState(bool force)
    {
        if (!force &&
            _hasAppliedState &&
            _lastAppliedStateVersion == _presentation.StateVersion &&
            _lastAppliedActionSequence == _presentation.ActionSequence)
        {
            return;
        }

        _hasAppliedState = true;
        _lastAppliedStateVersion = _presentation.StateVersion;
        _lastAppliedActionSequence = _presentation.ActionSequence;
        if (!_animationTree.Active || _animationPlayback is null)
        {
            return;
        }

        PlayerPresentationState state = _presentation.CurrentState;
        if (state == PlayerPresentationState.Disabled)
        {
            state = PlayerPresentationState.Idle;
        }

        if (!IsClipAvailable(state))
        {
            // Missing one-shots deliberately do not play an unrelated loop.
            // Missing locomotion may safely hold or return to a configured Idle.
            if (IsOneShotState(state) || !IsClipAvailable(PlayerPresentationState.Idle))
            {
                return;
            }

            state = PlayerPresentationState.Idle;
        }

        StringName treeState = GetTreeStateName(state);
        if (IsOneShotState(state))
        {
            _animationPlayback.Start(treeState, reset: true);
        }
        else
        {
            _animationPlayback.Travel(treeState, resetOnTeleport: true);
        }
    }

    private void PublishChangesIfNeeded(double delta)
    {
        if (_lastPublishedStateVersion != _presentation.StateVersion ||
            _lastPublishedActionSequence != _presentation.ActionSequence)
        {
            _lastPublishedStateVersion = _presentation.StateVersion;
            _lastPublishedActionSequence = _presentation.ActionSequence;
            SafeEventPublisher.Publish(
                PresentationChanged,
                $"{nameof(PlayerVisualController3D)}.{nameof(PresentationChanged)}");
            PublishDebugSnapshot(force: true);
        }

        if (!double.IsFinite(delta) || delta <= 0.0)
        {
            return;
        }

        _debugElapsedSeconds += delta;
        if (_debugElapsedSeconds < DebugUpdateIntervalSeconds)
        {
            return;
        }

        _debugElapsedSeconds -= DebugUpdateIntervalSeconds;
        double maximumDebt =
            DebugUpdateIntervalSeconds * MaximumDebugCatchUpTicks;
        if (_debugElapsedSeconds > maximumDebt)
        {
            // Debug publication is non-critical. Excess debt after a long frame
            // is discarded while preserving one bounded interval of remainder.
            _debugElapsedSeconds %= DebugUpdateIntervalSeconds;
        }

        PublishDebugSnapshot(force: false);
    }

    private void PublishDebugSnapshot(bool force)
    {
        PlayerVisualDebugSnapshot snapshot = new(
            _presentation.CurrentState,
            _presentation.ActiveAction,
            _presentation.CurrentProfile,
            _locomotion.LocalBlend,
            Mathf.RadToDeg(_visualYaw),
            IsUsingDevelopmentFallback
                ? PlayerVisualSource.DevelopmentFallback
                : PlayerVisualSource.ImportedModel,
            HasValidSocketHierarchy,
            MissingClipCount);
        if (!force && _hasDebugSnapshot && snapshot == _lastDebugSnapshot)
        {
            return;
        }

        _hasDebugSnapshot = true;
        _lastDebugSnapshot = snapshot;
        SafeEventPublisher.Publish(
            DebugSnapshotChanged,
            snapshot,
            $"{nameof(PlayerVisualController3D)}.{nameof(DebugSnapshotChanged)}");
    }

    private void OnMovementModeChanged(
        Gameplay.Movement.MovementMode previousMode,
        Gameplay.Movement.MovementMode currentMode)
    {
        SynchronizeAuthoritativeState();
    }

    private void OnPostureChanged(
        Gameplay.Movement.MovementMode previousPosture,
        Gameplay.Movement.MovementMode currentPosture)
    {
        SynchronizeAuthoritativeState();
    }

    private void OnInputStateChanged()
    {
        SynchronizeAuthoritativeState();
    }

    private void OnShotResolved(FirearmShotOccurrence3D occurrence)
    {
        if (!_presentation.ObserveCompletedShot())
        {
            return;
        }

        _muzzleFlashSecondsRemaining = MuzzleFlashSeconds;
        _muzzleFlash.Visible = true;
        _recoilStrength = 1.0f;
    }

    private void OnReloadChanged(ReloadResult result)
    {
        _presentation.ObserveReload(result.Status);
    }

    private void OnDamaged(DamageInfo damage, HealthChangeResult result)
    {
        _presentation.ObserveCompletedDamage(result.Changed, result.CausedDeath);
    }

    private void OnDied(DamageInfo damage, HealthChangeResult result)
    {
        _presentation.ObserveDeath();
    }

    private void Unbind()
    {
        if (_player is not null)
        {
            _player.MovementModeChanged -= OnMovementModeChanged;
            _player.PostureChanged -= OnPostureChanged;
            _player.InputStateChanged -= OnInputStateChanged;
        }

        if (_weaponController is not null)
        {
            _weaponController.ShotResolved -= OnShotResolved;
            _weaponController.ReloadChanged -= OnReloadChanged;
        }

        if (_health is not null)
        {
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }

        _player = null;
        _aimController = null;
        _weaponController = null;
        _health = null;
        _aimPivot = null;
        _isBound = false;
        SetProcess(false);
    }

    private bool IsClipAvailable(PlayerPresentationState state)
    {
        int index = (int)state;
        return index >= 0 &&
               index < _clipAvailable.Length &&
               _clipAvailable[index];
    }

    private static bool IsOneShotState(PlayerPresentationState state)
    {
        return state is PlayerPresentationState.Fire or
            PlayerPresentationState.Reload or
            PlayerPresentationState.HitReaction or
            PlayerPresentationState.Death;
    }

    private static bool IsHorizontallyMoving(Vector3 velocity)
    {
        return velocity.IsFinite() &&
               new Vector2(velocity.X, velocity.Z).LengthSquared() > 0.01f;
    }

    private static StringName GetTreeStateName(PlayerPresentationState state)
    {
        return state switch
        {
            PlayerPresentationState.Idle => IdleTreeState,
            PlayerPresentationState.Walk => WalkTreeState,
            PlayerPresentationState.Sprint => SprintTreeState,
            PlayerPresentationState.CrouchIdle => CrouchIdleTreeState,
            PlayerPresentationState.CrouchWalk => CrouchWalkTreeState,
            PlayerPresentationState.CrawlIdle => CrawlIdleTreeState,
            PlayerPresentationState.CrawlMove => CrawlMoveTreeState,
            PlayerPresentationState.Fire => FireTreeState,
            PlayerPresentationState.Reload => ReloadTreeState,
            PlayerPresentationState.HitReaction => HitReactionTreeState,
            PlayerPresentationState.Death => DeathTreeState,
            _ => IdleTreeState,
        };
    }

    private static StringName? CacheParameterPath(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : new StringName(value.Trim());
    }

    private static float ExponentialBlend(float response, float delta)
    {
        return 1.0f - MathF.Exp(-response * delta);
    }

    private void ValidateConfiguration()
    {
        ValidateRange(IdleStopSpeed, 0.0f, 2.0f, nameof(IdleStopSpeed));
        ValidateRange(IdleStartSpeed, 0.01f, 3.0f, nameof(IdleStartSpeed));
        if (IdleStartSpeed <= IdleStopSpeed)
        {
            throw new InvalidOperationException(
                $"{nameof(IdleStartSpeed)} must exceed {nameof(IdleStopSpeed)}.");
        }

        ValidateRange(VisualYawResponse, 1.0f, 40.0f, nameof(VisualYawResponse));
        ValidateRange(PostureResponse, 1.0f, 40.0f, nameof(PostureResponse));
        ValidateRange(MinimumAnimationSpeed, 0.1f, 2.0f,
            nameof(MinimumAnimationSpeed));
        ValidateRange(MaximumAnimationSpeed, 0.1f, 3.0f,
            nameof(MaximumAnimationSpeed));
        if (MaximumAnimationSpeed < MinimumAnimationSpeed)
        {
            throw new InvalidOperationException(
                "Maximum animation speed cannot be below the minimum.");
        }

        ValidateRange(FirePresentationSeconds, 0.01, 1.0,
            nameof(FirePresentationSeconds));
        ValidateRange(HitReactionSeconds, 0.01, 1.0,
            nameof(HitReactionSeconds));
        ValidateRange(MuzzleFlashSeconds, 0.01, 0.5,
            nameof(MuzzleFlashSeconds));
        ValidateRange(RecoilDistance, 0.0f, 0.3f, nameof(RecoilDistance));
        ValidateRange(RecoilRecovery, 1.0f, 50.0f, nameof(RecoilRecovery));
        ValidateRange(DebugUpdateIntervalSeconds, 0.05, 1.0,
            nameof(DebugUpdateIntervalSeconds));
    }

    private static void ValidateRange(
        double value,
        double minimum,
        double maximum,
        string propertyName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{propertyName} must be finite and between {minimum} and {maximum}.");
        }
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PlayerVisualController3D)} on '{Name}' requires '{path}'.");
    }
}
