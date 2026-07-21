using System;
using LineZero.Core.Events;

namespace LineZero.Gameplay.Power;

internal readonly record struct PowerCircuitInstallationPlan(
    PowerCircuitModel Circuit);

public sealed class PowerCircuitModel
{
    private bool _installationNotificationsPublished;

    public bool HasInstalledFuse { get; private set; }

    public bool IsPowered { get; private set; }

    public event Action? Changed;

    public event Action? PowerRestored;

    public bool TryInstallFuse()
    {
        if (!TryPrepareInstallation(out PowerCircuitInstallationPlan plan))
        {
            return false;
        }

        ApplyWithoutNotification(plan);
        PublishInstallationCompleted();
        return true;
    }

    internal bool CanInstallFuse =>
        !HasInstalledFuse &&
        !IsPowered &&
        !_installationNotificationsPublished;

    internal bool TryPrepareInstallation(
        out PowerCircuitInstallationPlan plan)
    {
        if (!CanInstallFuse)
        {
            plan = default;
            return false;
        }

        plan = new PowerCircuitInstallationPlan(this);
        return true;
    }

    internal bool CanApply(PowerCircuitInstallationPlan plan)
    {
        return ReferenceEquals(plan.Circuit, this) && CanInstallFuse;
    }

    internal void ApplyWithoutNotification(PowerCircuitInstallationPlan plan)
    {
        if (!CanApply(plan))
        {
            throw new InvalidOperationException(
                "The prepared power-circuit installation is no longer valid.");
        }

        HasInstalledFuse = true;
        IsPowered = true;
    }

    internal void PublishInstallationCompleted()
    {
        if (!HasInstalledFuse || !IsPowered)
        {
            throw new InvalidOperationException(
                "Power restoration cannot be published before the circuit is powered.");
        }

        if (_installationNotificationsPublished)
        {
            return;
        }

        _installationNotificationsPublished = true;
        SafeEventPublisher.Publish(
            Changed,
            $"{nameof(PowerCircuitModel)}.{nameof(Changed)}");
        SafeEventPublisher.Publish(
            PowerRestored,
            $"{nameof(PowerCircuitModel)}.{nameof(PowerRestored)}");
    }
}
