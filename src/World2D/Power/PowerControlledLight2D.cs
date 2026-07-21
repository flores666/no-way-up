using System;
using Godot;
using LineZero.Gameplay.Power;
using LineZero.World2D.Perception;

namespace LineZero.World2D.Power;

public sealed partial class PowerControlledLight2D : Node2D
{
    private static readonly Color OfflineColor = new(0.58f, 0.12f, 0.09f, 1.0f);
    private static readonly Color OnlineColor = new(0.37f, 0.92f, 0.66f, 1.0f);

    private PointLight2D _poweredLight = null!;
    private Polygon2D _powerIndicator = null!;
    private Polygon2D _lightOverlay = null!;
    private LightExposureZone2D _visibilityZone = null!;
    private PowerCircuitModel? _circuit;

    public override void _Ready()
    {
        _poweredLight = RequireNode<PointLight2D>("%PoweredLight");
        _powerIndicator = RequireNode<Polygon2D>("%PowerIndicator");
        _lightOverlay = RequireNode<Polygon2D>("%LightOverlay");
        _visibilityZone = RequireNode<LightExposureZone2D>("%PoweredVisibilityZone");
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
                $"{nameof(PowerControlledLight2D)} on '{Name}' already has a power circuit.");
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
            !GodotObject.IsInstanceValid(_lightOverlay) ||
            !GodotObject.IsInstanceValid(_visibilityZone))
        {
            return;
        }

        _poweredLight.Enabled = isPowered;
        _powerIndicator.Color = isPowered ? OnlineColor : OfflineColor;
        _lightOverlay.Visible = isPowered;
        _visibilityZone.SetExposureEnabled(isPowered);
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(PowerControlledLight2D)} on '{Name}' requires '{path}'.");
    }
}
