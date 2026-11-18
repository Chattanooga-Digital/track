namespace Track.Website.Api.v0.Models.Backups;

public class BackupOverviewModel
{
    public bool TargetSet { get; set; }
    public bool MasterKeySet { get; set; }
    public bool Paused { get; set; }
    public int Included { get; set; }

    /// <summary>Included volumes with a good backup inside the stale window.</summary>
    public int Protected { get; set; }
    public int NeedsAttention { get; set; }
    public int Excluded { get; set; }
    public long UploadedLast24HoursBytes { get; set; }

    /// <summary>Approved, online nodes a volume can be restored onto.</summary>
    public List<string> ReadyNodes { get; set; } = [];

    public List<BackupGroupModel> Groups { get; set; } = [];
}

public class BackupGroupModel
{
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public bool IsShared { get; set; }
    public required string RepositoryKey { get; set; }
    public long? RepositorySizeBytes { get; set; }
    public List<BackupVolumeModel> Volumes { get; set; } = [];
}
