using System;
using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Noise;
using LineZero.Gameplay.Objectives;
using LineZero.Gameplay.Power;
using LineZero.World2D.Noise;

namespace LineZero.World2D.Interaction;

public sealed partial class FuseBox2D : Interactable2D, INoiseEmitter2D
{
    private const float InstallationNoiseIntensity = 1.25f;

    private readonly FuseInstallationService _installationService = new();

    private StaticBody2D _solidBody = null!;
    private Polygon2D _statusIndicator = null!;
    private PowerCircuitModel? _circuit;
    private ObjectiveProgressModel? _objectives;
    private NoiseSystem2D? _noiseSystem;

    public override string InteractionPrompt => _circuit?.IsPowered == true
        ? "Power is online"
        : "Install replacement fuse";

    public override void _Ready()
    {
        base._Ready();
        _solidBody = GetNodeOrNull<StaticBody2D>("SolidBody")
            ?? throw new InvalidOperationException(
                $"{nameof(FuseBox2D)} on '{Name}' requires a SolidBody node.");
        _statusIndicator = GetNodeOrNull<Polygon2D>("%StatusIndicator")
            ?? throw new InvalidOperationException(
                $"{nameof(FuseBox2D)} on '{Name}' requires a StatusIndicator node.");
        ApplyPresentation();
    }

    public override void _ExitTree()
    {
        if (_circuit is not null)
        {
            _circuit.Changed -= OnCircuitChanged;
        }

        _circuit = null;
        _objectives = null;
        _noiseSystem = null;
    }

    public override bool CanInteract(InteractionContext context)
    {
        if (_circuit is null || _objectives is null)
        {
            return false;
        }

        return context.Actor is not IHealthOwner healthOwner || healthOwner.Health.IsAlive;
    }

    public override InteractionResult Interact(InteractionContext context)
    {
        PowerCircuitModel circuit = _circuit
            ?? throw new InvalidOperationException(
                $"{nameof(FuseBox2D)} on '{Name}' has no bound power circuit.");
        ObjectiveProgressModel objectives = _objectives
            ?? throw new InvalidOperationException(
                $"{nameof(FuseBox2D)} on '{Name}' has no bound objective model.");

        if (context.Actor is IHealthOwner healthOwner && healthOwner.Health.IsDead)
        {
            return InteractionResult.None;
        }

        if (context.Actor is not IInventoryOwner inventoryOwner)
        {
            return InteractionResult.WithMessage(
                "This actor cannot install inventory items.");
        }

        NoiseSystem2D noiseSystem = _noiseSystem
            ?? throw new InvalidOperationException(
                $"{nameof(FuseBox2D)} on '{Name}' has no bound noise system.");

        FuseInstallationResult result = _installationService.TryInstall(
            inventoryOwner.Inventory,
            circuit,
            objectives,
            canInstall: true);
        if (!result.Success)
        {
            return InteractionResult.WithMessage(result.Message);
        }

        noiseSystem.EmitNoise(
            context.Actor,
            NoiseKind.Interaction,
            InstallationNoiseIntensity,
            GlobalPosition,
            _solidBody,
            "Installing replacement fuse");
        return InteractionResult.WithMessage(result.Message);
    }

    public void BindPowerCircuit(PowerCircuitModel circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (_circuit is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(FuseBox2D)} on '{Name}' already has a power circuit.");
        }

        _circuit = circuit;
        _circuit.Changed += OnCircuitChanged;
        ApplyPresentation();
    }

    public void BindObjectives(ObjectiveProgressModel objectives)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        if (_objectives is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(FuseBox2D)} on '{Name}' already has an objective model.");
        }

        _objectives = objectives;
    }

    public void BindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(FuseBox2D)} on '{Name}' already has a noise system.");
        }

        if (!GodotObject.IsInstanceValid(noiseSystem) || !noiseSystem.IsInsideTree())
        {
            throw new ArgumentException("The noise system must be active.", nameof(noiseSystem));
        }

        _noiseSystem = noiseSystem;
    }

    public void UnbindNoiseSystem(NoiseSystem2D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (ReferenceEquals(_noiseSystem, noiseSystem))
        {
            _noiseSystem = null;
        }
    }

    private void OnCircuitChanged()
    {
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        if (!GodotObject.IsInstanceValid(_statusIndicator))
        {
            return;
        }

        _statusIndicator.Color = _circuit?.IsPowered == true
            ? new Color(0.27f, 0.92f, 0.48f, 1.0f)
            : new Color(0.74f, 0.16f, 0.12f, 1.0f);
    }
}
