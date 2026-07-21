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

public abstract partial class PlayableLevelController2D : Node2D
{
    private readonly List<MutantController2D> _mutants = new();
    private readonly List<DamageZone2D> _damageZones = new();
    private readonly List<INoiseEmitter2D> _noiseEmitters = new();
    private readonly List<PowerControlledLight2D> _poweredLights = new();

    private NoiseSystem2D? _noiseSystem;
    private bool _isInitialized;
    private bool _targetsBound;
    private PowerCircuitComponent _powerCircuit = null!;
    private FuseBox2D _fuseBox = null!;
    private SlidingDoor2D _emergencyExitDoor = null!;
    private ObjectiveExitZone2D _exitZone = null!;

    public IReadOnlyList<MutantController2D> Mutants => _mutants;

    public IReadOnlyList<PowerControlledLight2D> PoweredLights => _poweredLights;

    public PowerCircuitComponent PowerCircuit => _powerCircuit;

    public FuseBox2D FuseBox => _fuseBox;

    public SlidingDoor2D EmergencyExitDoor => _emergencyExitDoor;

    public ObjectiveExitZone2D ExitZone => _exitZone;

    public override void _Ready()
    {
        NavigationRegion2D navigationRegion = RequireNode<NavigationRegion2D>(
            "%NavigationRegion2D");
        if (!navigationRegion.Enabled || navigationRegion.NavigationLayers == 0)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' requires an enabled navigation region.");
        }

        NavigationPolygon navigationPolygon = navigationRegion.NavigationPolygon
            ?? throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' requires a navigation polygon.");
        if (navigationPolygon.GetPolygonCount() == 0)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' has no walkable navigation cells.");
        }

        CollectMutants();
        CollectHazards();
        CollectNoiseEmitters();
        CollectPoweredLights();

        _powerCircuit = RequireNode<PowerCircuitComponent>("%PowerCircuitComponent");
        _fuseBox = RequireNode<FuseBox2D>("%MaintenanceFuseBox");
        _emergencyExitDoor = RequireNode<SlidingDoor2D>("%EmergencyExitDoor");
        _exitZone = RequireNode<ObjectiveExitZone2D>("%ObjectiveExitZone");
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
        _poweredLights.Clear();
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

        EnsureInitialized();
        if (_targetsBound)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} already bound mutant targets.");
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
        EnsureInitialized();

        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} already has a noise system.");
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

    private void CollectMutants()
    {
        Node container = RequireNode<Node>("%Mutants");
        for (int index = 0; index < container.GetChildCount(); index++)
        {
            Node child = container.GetChild(index);
            if (child is not MutantController2D mutant)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} requires every direct Mutants child " +
                    "to be a MutantController2D.");
            }

            _mutants.Add(mutant);
        }
    }

    private void CollectHazards()
    {
        Node container = RequireNode<Node>("Hazards");
        for (int index = 0; index < container.GetChildCount(); index++)
        {
            if (container.GetChild(index) is DamageZone2D damageZone)
            {
                _damageZones.Add(damageZone);
            }
        }
    }

    private void CollectNoiseEmitters()
    {
        Node container = RequireNode<Node>("Interactions");
        for (int index = 0; index < container.GetChildCount(); index++)
        {
            if (container.GetChild(index) is INoiseEmitter2D emitter)
            {
                _noiseEmitters.Add(emitter);
            }
        }
    }

    private void CollectPoweredLights()
    {
        Node container = RequireNode<Node>("PowerSystems");
        for (int index = 0; index < container.GetChildCount(); index++)
        {
            if (container.GetChild(index) is PowerControlledLight2D poweredLight)
            {
                _poweredLights.Add(poweredLight);
            }
        }

        if (_poweredLights.Count == 0)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} requires at least one powered light.");
        }
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} is not ready for composition binding.");
        }
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{GetType().Name} on '{Name}' requires '{path}'.");
    }
}
