namespace Track.Enums;

public enum BackupJobStatus
{
    Queued = 0,

    /// <summary>Handed to an agent, which has not fetched it yet.</summary>
    Dispatched = 1,

    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
