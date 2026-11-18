using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>Index of the restic snapshots in every repository, so pages never list the bucket.</summary>
public class BackupSnapshot : IEntityTypeConfiguration<BackupSnapshot>
{
    public const string Tag = "track";

    /// <summary>Safety snapshots taken before a replace. Tagged apart so they never take a day's slot in the retention policy.</summary>
    public const string PreRestoreTag = "track-pre-restore";

    public long Id { get; set; }
    public required string SnapshotId { get; set; }
    public required string RepositoryKey { get; set; }

    /// <summary>The restic host, which is the catalog key.</summary>
    public required string VolumeKey { get; set; }

    public required string VolumeName { get; set; }
    public DateTime Time { get; set; }
    public string? NodeName { get; set; }
    public string? StackName { get; set; }

    /// <summary>Images of the containers using the volume when it was backed up, comma separated.</summary>
    public string? Images { get; set; }

    /// <summary>Paths inside the volume, comma separated; empty when the whole volume was backed up.</summary>
    public string Paths { get; set; } = "";

    public long? BytesAdded { get; set; }
    public long? TotalBytes { get; set; }
    public bool PreRestore { get; set; }
    public long? JobId { get; set; }

    public void Configure(EntityTypeBuilder<BackupSnapshot> builder)
    {
        builder.Property(s => s.SnapshotId).HasMaxLength(64);
        builder.Property(s => s.RepositoryKey).HasMaxLength(64);
        builder.Property(s => s.VolumeKey).HasMaxLength(320);
        builder.Property(s => s.VolumeName).HasMaxLength(255);
        builder.Property(s => s.NodeName).HasMaxLength(200);
        builder.Property(s => s.StackName).HasMaxLength(200);
        builder.Property(s => s.Images).HasMaxLength(2000);
        builder.Property(s => s.Paths).HasMaxLength(2000);
        builder.HasIndex(s => new { s.RepositoryKey, s.SnapshotId }).IsUnique();
        builder.HasIndex(s => new { s.VolumeKey, s.Time });
        builder.HasIndex(s => new { s.VolumeName, s.Time });
    }
}
