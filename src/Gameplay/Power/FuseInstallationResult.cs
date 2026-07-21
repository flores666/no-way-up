namespace LineZero.Gameplay.Power;

public readonly record struct FuseInstallationResult(
    bool Success,
    bool FuseConsumed,
    bool PowerRestored,
    string Message);
