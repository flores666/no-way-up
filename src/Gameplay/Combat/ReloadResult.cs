using System;

namespace LineZero.Gameplay.Combat;

public enum ReloadStatus
{
    Started,
    Completed,
    Canceled,
    AlreadyReloading,
    MagazineFull,
    NoReserveAmmo,
    NotReloading,
    CombatDisabled,
    OwnerDead,
}

public sealed class ReloadResult
{
    private ReloadResult(
        ReloadStatus status,
        bool stateChanged,
        int suppliedRounds,
        int loadedRounds,
        int currentMagazineAmmo,
        string message)
    {
        if (suppliedRounds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(suppliedRounds),
                "Supplied reload rounds cannot be negative.");
        }

        if (loadedRounds < 0 || loadedRounds > suppliedRounds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(loadedRounds),
                "Loaded rounds must be within the supplied quantity.");
        }

        if (currentMagazineAmmo < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentMagazineAmmo),
                "Current magazine ammunition cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A reload result requires a message.",
                nameof(message));
        }

        bool expectedStateChange = status is ReloadStatus.Started or
            ReloadStatus.Completed or ReloadStatus.Canceled;
        if (stateChanged != expectedStateChange)
        {
            throw new ArgumentException(
                "Reload state-change reporting does not match its status.",
                nameof(stateChanged));
        }

        Status = status;
        StateChanged = stateChanged;
        SuppliedRounds = suppliedRounds;
        LoadedRounds = loadedRounds;
        CurrentMagazineAmmo = currentMagazineAmmo;
        Message = message.Trim();
    }

    public ReloadStatus Status { get; }

    public bool Success => Status is ReloadStatus.Started or ReloadStatus.Completed;

    public bool StateChanged { get; }

    public int SuppliedRounds { get; }

    public int LoadedRounds { get; }

    public int CurrentMagazineAmmo { get; }

    public string Message { get; }

    public static ReloadResult Changed(
        ReloadStatus status,
        int currentMagazineAmmo,
        string message,
        int suppliedRounds = 0,
        int loadedRounds = 0)
    {
        if (status is not (ReloadStatus.Started or ReloadStatus.Completed or
            ReloadStatus.Canceled))
        {
            throw new ArgumentException(
                "The changed factory requires a state-changing reload status.",
                nameof(status));
        }

        return new ReloadResult(
            status,
            stateChanged: true,
            suppliedRounds,
            loadedRounds,
            currentMagazineAmmo,
            message);
    }

    public static ReloadResult Rejected(
        ReloadStatus status,
        int currentMagazineAmmo,
        string message)
    {
        if (status is ReloadStatus.Started or ReloadStatus.Completed or
            ReloadStatus.Canceled)
        {
            throw new ArgumentException(
                "The rejected factory requires a non-changing reload status.",
                nameof(status));
        }

        return new ReloadResult(
            status,
            stateChanged: false,
            suppliedRounds: 0,
            loadedRounds: 0,
            currentMagazineAmmo,
            message);
    }
}
