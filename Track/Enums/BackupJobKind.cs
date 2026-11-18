namespace Track.Enums;

public enum BackupJobKind
{
    /// <summary>Agent: hooks, then restic backup of one volume.</summary>
    Backup = 0,

    /// <summary>Agent: restic restore of a snapshot into a new or existing volume.</summary>
    Restore = 1,

    /// <summary>Agent: run restore hooks in a service's running container.</summary>
    Hook = 2,

    /// <summary>Manager agent: set swarm service replica counts and wait for them.</summary>
    Scale = 3,

    /// <summary>Central: forget and prune one repository.</summary>
    Prune = 4,

    /// <summary>Central: verify one repository.</summary>
    Check = 5,

    /// <summary>Central: the parent of the steps that replace a volume's live data.</summary>
    Replace = 6,
}
