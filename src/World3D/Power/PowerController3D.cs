using System;
using Godot;
using LineZero.Gameplay.Power;

namespace LineZero.World3D.Power;

public sealed partial class PowerController3D : Node3D
{
    private static readonly Color OfflineColor =
        new(0.72f, 0.12f, 0.08f, 1.0f);
    private static readonly Color OnlineColor =
        new(0.18f, 0.92f, 0.42f, 1.0f);

    private MeshInstance3D _statusIndicator = null!;
    private StandardMaterial3D _statusMaterial = null!;

    public PowerCircuitModel Model { get; } = new();

    public override void _Ready()
    {
        _statusIndicator = GetNodeOrNull<MeshInstance3D>("%PowerStatusIndicator3D")
            ?? throw new InvalidOperationException(
                $"{nameof(PowerController3D)} on '{Name}' requires PowerStatusIndicator3D.");
        if (_statusIndicator.Mesh is null)
        {
            throw new InvalidOperationException(
                $"{nameof(PowerController3D)} on '{Name}' requires an indicator mesh.");
        }

        _statusMaterial = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 1.5f,
            Roughness = 0.55f
        };
        _statusIndicator.MaterialOverride = _statusMaterial;
        Model.Changed += OnCircuitChanged;
        ApplyPresentation();
    }

    public override void _ExitTree()
    {
        Model.Changed -= OnCircuitChanged;
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

        Color color = Model.IsPowered ? OnlineColor : OfflineColor;
        _statusMaterial.AlbedoColor = color;
        _statusMaterial.Emission = color.Darkened(0.2f);
    }
}
