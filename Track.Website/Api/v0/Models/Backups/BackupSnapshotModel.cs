using Track.Enums;

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
    public DatabaseEngine? DumpEngine { get; set; }

    /// <summary>Taken while the volume was used with images that aren't running now.</summary>
    public bool DifferentImage { get; set; }
}

public class SnapshotListingModel
{
    /// <summary>Relative to the volume's root; empty for the root itself.</summary>
    public required string Path { get; set; }

    public List<SnapshotEntryModel> Entries { get; set; } = [];
}

public class SnapshotEntryModel
{
    public required string Name { get; set; }
    public bool IsDirectory { get; set; }
    public long? Size { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
