namespace Track.Enums;

public enum BackupTrigger
{
    Schedule = 0,
    Manual = 1,

    /// <summary>A step of a restore, which skips the concurrency caps.</summary>
    Restore = 2,
}
