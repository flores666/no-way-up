using System;
using Godot;
using LineZero.Gameplay.Power;
using LineZero.World3D.Perception;

namespace LineZero.World3D.Power;

public sealed partial class PowerControlledLight3D : Node3D
{
    private static readonly Color OfflineColor =
        new(0.58f, 0.1f, 0.06f, 1.0f);
    private static readonly Color OnlineColor =
        new(0.35f, 0.92f, 0.62f, 1.0f);

    private OmniLight3D _poweredLight = null!;
    private MeshInstance3D _powerIndicator = null!;
    private StandardMaterial3D _indicatorMaterial = null!;
    private LightExposureZone3D _visibilityZone = null!;
    private PowerCircuitModel? _circuit;

    public bool IsPoweredPresentationActive => _poweredLight.Visible;

    public override void _Ready()
    {
        _poweredLight = RequireNode<OmniLight3D>("%PoweredLight3D");
        _powerIndicator = RequireNode<MeshInstance3D>("%PoweredLightIndicator3D");
        _visibilityZone = RequireNode<LightExposureZone3D>(
            "%PoweredVisibilityZone3D");
        if (_powerIndicator.Mesh is null)
        {
            throw new InvalidOperationException(
                $"{nameof(PowerControlledLight3D)} on '{Name}' requires an indicator mesh.");
        }

        _indicatorMaterial = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 1.3f,
            Roughness = 0.62f
        };
        _powerIndicator.MaterialOverride = _indicatorMaterial;
        ApplyPowerState(isPowered: false);
    }

    public override void _ExitTree()
    {
        if (_circuit is not null)
        {
            _circuit.Changed -= OnCircuitChanged;
        }

        _circuit = null;
    }

    public void BindPowerCircuit(PowerCircuitModel circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (_circuit is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(PowerControlledLight3D)} on '{Name}' is already bound.");
        }

        _circuit = circuit;
        _circuit.Changed += OnCircuitChanged;
        ApplyPowerState(_circuit.IsPowered);
    }

    private void OnCircuitChanged()
    {
        PowerCircuitModel circuit = _circuit
            ?? throw new InvalidOperationException("Power circuit binding was lost.");
        ApplyPowerState(circuit.IsPowered);
    }

    private void ApplyPowerState(bool isPowered)
    {
        if (!GodotObject.IsInstanceValid(_poweredLight) ||
            !GodotObject.IsInstanceValid(_powerIndicator) ||
            !GodotObject.IsInstanceValid(_indicatorMaterial) ||
            !GodotObject.IsInstanceValid(_visibilityZone))
        {
            return;
        }

        _poweredLight.Visible = isPowered;
        Color color = isPowered ? OnlineColor : OfflineColor;
        _indicatorMaterial.AlbedoColor = color;
        _indicatorMaterial.Emission = color.Darkened(0.2f);
        _visibilityZone.SetExposureEnabled(isPowered);
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PowerControlledLight3D)} on '{Name}' requires '{path}'.");
    }
}
