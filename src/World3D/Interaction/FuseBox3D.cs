using System;
using Godot;
using LineZero.Gameplay.Health;
using LineZero.Gameplay.Interaction;
using LineZero.Gameplay.Inventory;
using LineZero.Gameplay.Noise;
using LineZero.Gameplay.Objectives;
using LineZero.Gameplay.Power;
using LineZero.World3D.Noise;

namespace LineZero.World3D.Interaction;

public sealed partial class FuseBox3D : Interactable3D
{
    private const float InstallationNoiseIntensity = 1.25f;

    private static readonly Color OfflineColor =
        new(0.74f, 0.14f, 0.09f, 1.0f);
    private static readonly Color OnlineColor =
        new(0.25f, 0.92f, 0.45f, 1.0f);

    private readonly FuseInstallationService _installationService = new();

    private StaticBody3D _solidBody = null!;
    private MeshInstance3D _statusIndicator = null!;
    private StandardMaterial3D _statusMaterial = null!;
    private PowerCircuitModel? _circuit;
    private ObjectiveProgressModel? _objectives;
    private NoiseSystem3D? _noiseSystem;

    public override string InteractionPrompt => _circuit?.IsPowered == true
        ? "Power is online"
        : "Install replacement fuse";

    public override CollisionObject3D? InteractionOccluder => _solidBody;

    public override void _Ready()
    {
        base._Ready();
        _solidBody = GetNodeOrNull<StaticBody3D>("%FuseBoxSolidBody3D")
            ?? throw new InvalidOperationException(
                $"{nameof(FuseBox3D)} on '{Name}' requires FuseBoxSolidBody3D.");
        if (_solidBody.CollisionLayer != CollisionLayers3D.World)
        {
            throw new InvalidOperationException(
                $"{nameof(FuseBox3D)} on '{Name}' solid body must use the world layer.");
        }

        _statusIndicator = GetNodeOrNull<MeshInstance3D>("%FuseStatusIndicator3D")
            ?? throw new InvalidOperationException(
                $"{nameof(FuseBox3D)} on '{Name}' requires FuseStatusIndicator3D.");
        if (_statusIndicator.Mesh is null)
        {
            throw new InvalidOperationException("The fuse status indicator requires a mesh.");
        }

        _statusMaterial = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 1.5f,
            Roughness = 0.58f
        };
        _statusIndicator.MaterialOverride = _statusMaterial;
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
        if (_circuit is null || _objectives is null || _noiseSystem is null)
        {
            return false;
        }

        return context.Actor is not IHealthOwner healthOwner ||
               healthOwner.Health.IsAlive;
    }

    public override InteractionResult Interact(InteractionContext context)
    {
        PowerCircuitModel circuit = _circuit
            ?? throw new InvalidOperationException("The 3D fuse box has no power circuit.");
        ObjectiveProgressModel objectives = _objectives
            ?? throw new InvalidOperationException("The 3D fuse box has no objective model.");
        NoiseSystem3D noiseSystem = _noiseSystem
            ?? throw new InvalidOperationException("The 3D fuse box has no noise system.");

        if (context.Actor is IHealthOwner healthOwner && healthOwner.Health.IsDead)
        {
            return InteractionResult.None;
        }

        if (context.Actor is not IInventoryOwner inventoryOwner)
        {
            return InteractionResult.WithMessage(
                "This actor cannot install inventory items.");
        }

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
            GlobalPosition + (Vector3.Up * 0.8f),
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
                $"{nameof(FuseBox3D)} on '{Name}' already has a power circuit.");
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
                $"{nameof(FuseBox3D)} on '{Name}' already has objective state.");
        }

        _objectives = objectives;
    }

    public void BindNoiseSystem(NoiseSystem3D noiseSystem)
    {
        ArgumentNullException.ThrowIfNull(noiseSystem);
        if (_noiseSystem is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(FuseBox3D)} on '{Name}' already has a noise system.");
        }

        if (!GodotObject.IsInstanceValid(noiseSystem) || !noiseSystem.IsInsideTree())
        {
            throw new ArgumentException("The noise system must be active.", nameof(noiseSystem));
        }

        _noiseSystem = noiseSystem;
    }

    private void OnCircuitChanged()
    {
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        if (!GodotObject.IsInstanceValid(_statusIndicator) ||
            !GodotObject.IsInstanceValid(_statusMaterial))
        {
            return;
        }

        Color color = _circuit?.IsPowered == true ? OnlineColor : OfflineColor;
        _statusMaterial.AlbedoColor = color;
        _statusMaterial.Emission = color.Darkened(0.2f);
    }
}
