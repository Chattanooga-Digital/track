namespace Track.Enums;

public enum BackupVolumeStatus
{
    Ok,
    Queued,
    Running,
    Stale,
    Failed,
    Never,
    Excluded,
    StaleCopy,
    Ambiguous,
    NodeOffline,
}
