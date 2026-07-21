using System;
using Godot;
using LineZero.Gameplay.Health;

namespace LineZero.World2D.Combat;

public sealed partial class DamageableTarget2D : StaticBody2D, IHealthOwner
{
    private static readonly Color DamageFlashColor =
        new(1.0f, 0.68f, 0.22f, 1.0f);
    private static readonly Color DestroyedBodyColor =
        new(0.16f, 0.17f, 0.17f, 1.0f);
    private static readonly Color DestroyedCoreColor =
        new(0.28f, 0.09f, 0.07f, 1.0f);

    private HealthModel? _health;
    private Polygon2D _bodyVisual = null!;
    private Polygon2D _coreVisual = null!;
    private Label _healthLabel = null!;
    private Timer _damageFlashTimer = null!;
    private Color _aliveBodyColor;
    private Color _aliveCoreColor;

    public HealthModel Health => _health
        ?? throw new InvalidOperationException(
            $"{nameof(DamageableTarget2D)} on '{Name}' has no initialized health model.");

    public override void _Ready()
    {
        uint requiredLayers =
            CollisionLayers2D.World | CollisionLayers2D.DamageableTarget;
        if ((CollisionLayer & requiredLayers) != requiredLayers)
        {
            throw new InvalidOperationException(
                $"{nameof(DamageableTarget2D)} on '{Name}' must use World and " +
                "DamageableTarget collision layers.");
        }

        CollisionShape2D collisionShape = RequireNode<CollisionShape2D>(
            "%TargetCollision");
        if (collisionShape.Shape is null || collisionShape.Disabled)
        {
            throw new InvalidOperationException(
                $"{nameof(DamageableTarget2D)} on '{Name}' requires an enabled shape.");
        }

        HealthComponent healthComponent = RequireNode<HealthComponent>(
            "%HealthComponent");
        _bodyVisual = RequireNode<Polygon2D>("%BodyVisual");
        _coreVisual = RequireNode<Polygon2D>("%CoreVisual");
        _healthLabel = RequireNode<Label>("%TargetHealthLabel");
        _damageFlashTimer = RequireNode<Timer>("%DamageFlashTimer");

        if (!_damageFlashTimer.OneShot)
        {
            throw new InvalidOperationException(
                $"{nameof(DamageableTarget2D)} on '{Name}' requires a one-shot flash timer.");
        }

        _health = healthComponent.Health;
        _aliveBodyColor = _bodyVisual.Color;
        _aliveCoreColor = _coreVisual.Color;
        _health.Changed += OnHealthChanged;
        _health.Damaged += OnDamaged;
        _health.Died += OnDied;
        _damageFlashTimer.Timeout += OnDamageFlashTimeout;
        RefreshHealthDisplay();
    }

    public override void _ExitTree()
    {
        if (_health is not null)
        {
            _health.Changed -= OnHealthChanged;
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }

        if (GodotObject.IsInstanceValid(_damageFlashTimer))
        {
            _damageFlashTimer.Timeout -= OnDamageFlashTimeout;
        }
    }

    private void OnHealthChanged(HealthChangeResult result)
    {
        RefreshHealthDisplay();
    }

    private void OnDamaged(DamageInfo damage, HealthChangeResult result)
    {
        if (result.CausedDeath)
        {
            return;
        }

        _bodyVisual.Color = DamageFlashColor;
        _coreVisual.Color = Colors.White;
        _damageFlashTimer.Stop();
        _damageFlashTimer.Start();
    }

    private void OnDied(DamageInfo damage, HealthChangeResult result)
    {
        _damageFlashTimer.Stop();
        _bodyVisual.Color = DestroyedBodyColor;
        _coreVisual.Color = DestroyedCoreColor;
        _healthLabel.Text = "TARGET DESTROYED";
        _healthLabel.Modulate = new Color(0.72f, 0.32f, 0.28f, 1.0f);
    }

    private void OnDamageFlashTimeout()
    {
        if (Health.IsDead)
        {
            return;
        }

        _bodyVisual.Color = _aliveBodyColor;
        _coreVisual.Color = _aliveCoreColor;
    }

    private void RefreshHealthDisplay()
    {
        if (Health.IsDead)
        {
            _healthLabel.Text = "TARGET DESTROYED";
            return;
        }

        _healthLabel.Text = $"TARGET {Health.CurrentHealth} / {Health.MaxHealth}";
    }

    private TNode RequireNode<TNode>(string path)
        where TNode : Node
    {
        return GetNodeOrNull<TNode>(path)
            ?? throw new InvalidOperationException(
                $"{nameof(DamageableTarget2D)} on '{Name}' requires '{path}'.");
    }
}
