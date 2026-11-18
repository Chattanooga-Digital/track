namespace Track.Website.Api.v0.Models.Backups;

public class BackupSnapshotModel
{
    public long Id { get; set; }
    public required string SnapshotId { get; set; }
    public required string RepositoryKey { get; set; }
    public required string VolumeKey { get; set; }
    public required string VolumeName { get; set; }
    public DateTime Time { get; set; }
    public string? NodeName { get; set; }
    public List<string> Images { get; set; } = [];
    public List<string> Paths { get; set; } = [];
    public long? BytesAdded { get; set; }
    public long? TotalBytes { get; set; }
    public bool PreRestore { get; set; }

    /// <summary>Taken while the volume was used with images that aren't running now.</summary>
    public bool DifferentImage { get; set; }
}
