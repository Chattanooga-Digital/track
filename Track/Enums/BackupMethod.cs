namespace Track.Enums;

public enum BackupMethod
{
    /// <summary>restic reads the volume's files.</summary>
    Files = 0,

    /// <summary>The database in the volume is dumped and the dump streamed into restic.</summary>
    Dump = 1,
}
