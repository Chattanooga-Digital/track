namespace Track.Enums;

public enum RestoreMode
{
    /// <summary>Restore into a fresh volume next to the original; nothing running is touched.</summary>
    NewVolume = 0,

    /// <summary>Snapshot, stop the services using the volume, restore over it, start them again.</summary>
    Replace = 1,
}
