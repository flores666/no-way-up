using System;
using System.Collections.Generic;
using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Perception;
using LineZero.World2D.Enemies;
using LineZero.World2D.Hazards;
using LineZero.World2D.Interaction;
using LineZero.World2D.Noise;
using LineZero.World2D.Power;

namespace LineZero.World2D.Levels;

public sealed partial class TestLevelController2D : Node2D
{
    private readonly List<MutantController2D> _mutants = new();
    private readonly List<DamageZone2D> _damageZones = new();
    private readonly List<INoiseEmitter2D> _noiseEmitters = new();

    private NavigationRegion2D _navigationRegion = null!;
    private NoiseSystem2D? _noiseSystem;
    private bool _isInitialized;
    private bool _targetsBound;
    private PowerCircuitComponent _powerCircuit = null!;
    private FuseBox2D _fuseBox = null!;
    private SlidingDoor2D _emergencyExitDoor = null!;
    private PowerControlledLight2D _poweredExitLighting = null!;
    private ObjectiveExitZone2D _exitZone = null!;

    public IReadOnlyList<MutantController2D> Mutants => _mutants;

    public PowerCircuitComponent PowerCircuit => _powerCircuit;

    public FuseBox2D FuseBox => _fuseBox;

    public SlidingDoor2D EmergencyExitDoor => _emergencyExitDoor;

    public PowerControlledLight2D PoweredExitLighting => _poweredExitLighting;

    public ObjectiveExitZone2D ExitZone => _exitZone;

    public override void _Ready()
    {
        _navigationRegion = RequireNode<NavigationRegion2D>("%NavigationRegion2D");
        if (!_navigationRegion.Enabled || _navigationRegion.NavigationLayers == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(TestLevelController2D)} on '{Name}' requires an enabled navigation region.");
        }

        NavigationPolygon navigationPolygon = _navigationRegion.NavigationPolygon
            ?? throw new InvalidOperationException(
                $"{nameof(TestLevelController2D)} on '{Name}' requires a navigation polygon.");
        if (navigationPolygon.GetPolygonCount() == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(TestLevelController2D)} on '{Name}' has no walkable navigation cells.");
        }

        Node mutantsContainer = RequireNode<Node>("%Mutants");
        for (int index = 0; index < mutantsContainer.GetChildCount(); index++)
        {
            Node child = mutantsContainer.GetChild(index);
            if (child is not MutantController2D mutant)
            {
                throw new InvalidOperationException(
                    $"{nameof(TestLevelController2D)} requires every direct Mutants child " +
                    "to be a MutantController2D.");
            }

            _mutants.Add(mutant);
        }

        _powerCircuit = RequireNode<PowerCircuitComponent>("%PowerCircuitComponent");
        _fuseBox = RequireNode<FuseBox2D>("%MaintenanceFuseBox");
        _emergencyExitDoor = RequireNode<SlidingDoor2D>("%EmergencyExitDoor");
        _poweredExitLighting = RequireNode<PowerControlledLight2D>("%PoweredExitLighting");
        _exitZone = RequireNode<ObjectiveExitZone2D>("%ObjectiveExitZone");

        Node hazardsContainer = RequireNode<Node>("Hazards");
        for (int index = 0; index < hazardsContainer.GetChildCount(); index++)
        {
            if (hazardsContainer.GetChild(index) is DamageZone2D damageZone)
            {
                _damageZones.Add(damageZone);
            }
        }

        Node interactionsContainer = RequireNode<Node>("Interactions");
        for (int index = 0; index < interactionsContainer.GetChildCount(); index++)
        {
            if (interactionsContainer.GetChild(index) is INoiseEmitter2D emitter)
            {
                _noiseEmitters.Add(emitter);
            }
        }

        _isInitialized = true;
    }

    public override void _ExitTree()
    {
        if (_noiseSystem is not null && GodotObject.IsInstanceValid(_noiseSystem))
        {
            for (int index = 0; index < _mutants.Count; index++)
            {
                _noiseSystem.UnregisterListener(_mutants[index]);
            }

            for (int index = 0; index < _noiseEmitters.Count; index++)
            {
                _noiseEmitters[index].UnbindNoiseSystem(_noiseSystem);
            }
        }

        _noiseSystem = null;
        _mutants.Clear();
        _damageZones.Clear();
        _noiseEmitters.Clear();
        _isInitialized = false;
        _targetsBound = false;
    }

    public void BindMutantTargets(
        Node2D target,
        IHealthOwner healthOwner,
        IVisibilityTarget visibilityTarget)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(healthOwner);
        ArgumentNullException.ThrowIfNull(visibilityTarget);

        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(TestLevelController2D)} has not initialized its mutant list.");
        }

        if (_targetsBound)
        {
            throw new InvalidOperationException(
                $"{nameof(TestLevelController2D)} already bound mutant targets.");
        }

        if (!GodotObject.IsInstanceValid(target) || !target.IsInsideTree())
        {
            throw new ArgumentException(
                "Mutants require an active target node.",
                nameof(target));
        }

        for (int index = 0; index < _mutants.Count; index++)
        {
            _mutants[index].BindTarget(target, healthOwner, visibilityTarget);
        }

        _targetsBound = true;
    }

    public void EnterTerminalPlayerState(HealthModel playerHealth)
    {
        ArgumentNullException.ThrowIfNull(playerHealth);

        for (int index = 0; index < _mutants.Count; index++)
        {
            _mutants[index].DisableTargeting();
        }

        for (int index = 0; index < _damageZones.Count; index++)
        {
            _damageZones[index].StopTracking(playerHealth);
        }
    }

    public void BindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(TestLevelController2D)} is not ready for noise binding.");
        }

        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(TestLevelController2D)} already has a noise system.");
        }

        if (!GodotObject.IsInstanceValid(noiseSystem) || !noiseSystem.IsInsideTree())
        {
            throw new ArgumentException("The noise system must be active.", nameof(noiseSystem));
        }

        for (int index = 0; index < _mutants.Count; index++)
        {
            noiseSystem.RegisterListener(_mutants[index]);
        }

        for (int index = 0; index < _noiseEmitters.Count; index++)
        {
            _noiseEmitters[index].BindNoiseSystem(noiseSystem);
        }

        _noiseSystem = noiseSystem;
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(TestLevelController2D)} on '{Name}' requires '{path}'.");
    }
}
