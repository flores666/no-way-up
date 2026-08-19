using System;
using Godot;
using LineZero.World2D.Combat;

namespace LineZero.UI;

public sealed partial class AimCrosshairController : Control
{
    private PlayerWeaponController2D? _weaponController;
    private Input.MouseModeEnum _mouseModeBeforeGameplay;
    private bool _ownsGameplayMouseMode;
    private bool _isGameplayActive = true;
    private bool _isUiMouseInteractionActive;

    [Export(PropertyHint.Range, "1.0,16.0,0.5")]
    public float ArmLength { get; set; } = 6.0f;

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
        TakeGameplayMouseMode();
        ApplyMouseMode();
    }

    public override void _ExitTree()
    {
        _weaponController = null;
        RefreshPresentation();
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

    public void Bind(PlayerWeaponController2D weaponController)
    {
        ArgumentNullException.ThrowIfNull(weaponController);
        if (_weaponController is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(AimCrosshairController)} on '{Name}' is already bound.");
        }

        _weaponController = weaponController;
        RefreshPresentation();
    }

    public void SetInteractionState(bool gameplayActive, bool uiMouseInteractionActive)
    {
        if (_isGameplayActive == gameplayActive &&
            _isUiMouseInteractionActive == uiMouseInteractionActive)
        {
            return;
        }

        _isGameplayActive = gameplayActive;
        _isUiMouseInteractionActive = uiMouseInteractionActive;
        ApplyMouseMode();
        RefreshPresentation();
    }

    private float ResolveSpreadGap(Vector2 mousePosition)
    {
        PlayerWeaponController2D weaponController = _weaponController
            ?? throw new InvalidOperationException(
                $"{nameof(AimCrosshairController)} on '{Name}' has no weapon binding.");

        Vector2 viewportCenter = Size * 0.5f;
        float aimDistance = mousePosition.DistanceTo(viewportCenter);
        float spreadDegrees = weaponController.IsAiming
            ? weaponController.State.Definition.AimedSpreadDegrees
            : weaponController.State.Definition.HipFireSpreadDegrees;
        return MathF.Tan(Mathf.DegToRad(spreadDegrees)) * aimDistance;
    }

    private void RefreshPresentation()
    {
        bool showCrosshair =
            _weaponController is not null &&
            _isGameplayActive &&
            !_isUiMouseInteractionActive;

        Visible = showCrosshair;
        SetProcess(showCrosshair);
        if (showCrosshair)
        {
            QueueRedraw();
        }
    }

    private void ValidateConfiguration()
    {
        if (!float.IsFinite(ArmLength) || ArmLength <= 0.0f ||
            !float.IsFinite(LineWidth) || LineWidth <= 0.0f ||
            !float.IsFinite(DotRadius) || DotRadius <= 0.0f)
        {
            throw new InvalidOperationException(
                $"{nameof(AimCrosshairController)} on '{Name}' has invalid crosshair geometry.");
        }
    }

    private void TakeGameplayMouseMode()
    {
        if (_ownsGameplayMouseMode)
        {
            return;
        }

        _mouseModeBeforeGameplay = Input.MouseMode;
        _ownsGameplayMouseMode = true;
    }

    private void ApplyMouseMode()
    {
        if (!_ownsGameplayMouseMode)
        {
            return;
        }

        Input.MouseMode = _isUiMouseInteractionActive
            ? Input.MouseModeEnum.Visible
            : Input.MouseModeEnum.Hidden;
    }

    private void RestoreMouseMode()
    {
        if (!_ownsGameplayMouseMode)
        {
            return;
        }

        Input.MouseMode = _mouseModeBeforeGameplay;
        _ownsGameplayMouseMode = false;
    }
}
