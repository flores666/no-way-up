using System;
using Godot;
using LineZero.World2D.Combat;

namespace LineZero.UI;

public sealed partial class AimCrosshairController : Control
{
    private PlayerWeaponController2D? _weaponController;
    private Input.MouseModeEnum _mouseModeBeforeAim;
    private bool _ownsMouseMode;

    [Export(PropertyHint.Range, "1.0,16.0,0.5")]
    public float ArmLength { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "1.0,16.0,0.5")]
    public float MinimumGap { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "4.0,32.0,0.5")]
    public float MaximumGap { get; set; } = 18.0f;

    [Export(PropertyHint.Range, "1.0,4.0,0.5")]
    public float LineWidth { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.5,4.0,0.5")]
    public float DotRadius { get; set; } = 1.5f;

    [Export]
    public Color CrosshairColor { get; set; } = new(0.94f, 0.96f, 0.92f, 0.95f);

    public override void _Ready()
    {
        ValidateConfiguration();
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        Unbind();
        RestoreMouseMode();
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Visible)
        {
            return;
        }

        Vector2 center = GetLocalMousePosition();
        float inner = ResolveSpreadGap(center);
        float outer = inner + ArmLength;

        DrawLine(
            center + new Vector2(-outer, 0.0f),
            center + new Vector2(-inner, 0.0f),
            CrosshairColor,
            LineWidth,
            antialiased: false);
        DrawLine(
            center + new Vector2(inner, 0.0f),
            center + new Vector2(outer, 0.0f),
            CrosshairColor,
            LineWidth,
            antialiased: false);
        DrawLine(
            center + new Vector2(0.0f, -outer),
            center + new Vector2(0.0f, -inner),
            CrosshairColor,
            LineWidth,
            antialiased: false);
        DrawLine(
            center + new Vector2(0.0f, inner),
            center + new Vector2(0.0f, outer),
            CrosshairColor,
            LineWidth,
            antialiased: false);
        DrawCircle(
            center,
            DotRadius,
            CrosshairColor,
            filled: true,
            width: -1.0f,
            antialiased: false);
    }

    private void ValidateConfiguration()
    {
        if (!float.IsFinite(ArmLength) || ArmLength <= 0.0f ||
            !float.IsFinite(MinimumGap) || MinimumGap < 0.0f ||
            !float.IsFinite(MaximumGap) || MaximumGap < MinimumGap ||
            !float.IsFinite(LineWidth) || LineWidth <= 0.0f ||
            !float.IsFinite(DotRadius) || DotRadius <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(AimCrosshairController)} on '{Name}' has invalid crosshair geometry.");
        }
    }

    private float ResolveSpreadGap(Vector2 mousePosition)
    {
        PlayerWeaponController2D weaponController = _weaponController
            ?? throw new InvalidOperationException(
                $"{nameof(AimCrosshairController)} on '{Name}' has no weapon binding.");

        Vector2 viewportCenter = Size * 0.5f;
        float aimDistance = mousePosition.DistanceTo(viewportCenter);
        float spreadRadians = Mathf.DegToRad(
            weaponController.State.Definition.AimedSpreadDegrees);
        float spreadRadius = MathF.Tan(spreadRadians) * aimDistance;
        return Math.Clamp(spreadRadius, MinimumGap, MaximumGap);
    }

    public void Bind(PlayerWeaponController2D weaponController)
    {
        ArgumentNullException.ThrowIfNull(weaponController);
        if (_weaponController is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(AimCrosshairController)} on '{Name}' is already bound.");
        }

        _weaponController = weaponController;
        _weaponController.AimingChanged += OnAimingChanged;
        ApplyAimingState(_weaponController.IsAiming);
    }

    private void Unbind()
    {
        if (_weaponController is not null)
        {
            _weaponController.AimingChanged -= OnAimingChanged;
            _weaponController = null;
        }

        ApplyAimingState(false);
    }

    private void OnAimingChanged(bool isAiming)
    {
        ApplyAimingState(isAiming);
    }

    private void ApplyAimingState(bool isAiming)
    {
        Visible = isAiming;
        SetProcess(isAiming);
        if (isAiming)
        {
            TakeMouseMode();
            QueueRedraw();
            return;
        }

        RestoreMouseMode();
    }

    private void TakeMouseMode()
    {
        if (_ownsMouseMode)
        {
            return;
        }

        _mouseModeBeforeAim = Input.MouseMode;
        Input.MouseMode = Input.MouseModeEnum.Hidden;
        _ownsMouseMode = true;
    }

    private void RestoreMouseMode()
    {
        if (!_ownsMouseMode)
        {
            return;
        }

        Input.MouseMode = _mouseModeBeforeAim;
        _ownsMouseMode = false;
    }
}
