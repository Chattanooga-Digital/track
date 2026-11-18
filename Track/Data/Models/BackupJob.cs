using Track.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

public class BackupJob : IEntityTypeConfiguration<BackupJob>
{
    public long Id { get; set; }
    public BackupJobKind Kind { get; set; }
    public BackupJobStatus Status { get; set; }
    public BackupTrigger Trigger { get; set; }

    /// <summary>The catalog key: the volume name, or <c>volume@node</c> for a volume that lives separately on several nodes.</summary>
    public string? VolumeKey { get; set; }

    public string? VolumeName { get; set; }
    public string? NodeName { get; set; }
    public string? RepositoryKey { get; set; }
    public string? StackName { get; set; }

    public long? ParentJobId { get; set; }
    public int Step { get; set; }

    /// <summary>Kind-specific inputs, such as the snapshot to restore or the replica counts to return to.</summary>
    public string? ParametersJson { get; set; }

    public DateTime QueuedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? LastProgressAt { get; set; }

    public double? PercentDone { get; set; }
    public long? BytesDone { get; set; }
    public long? BytesTotal { get; set; }

    public string? SnapshotId { get; set; }
    public long? BytesAdded { get; set; }
    public long? FilesNew { get; set; }
    public long? FilesChanged { get; set; }

    public string? Error { get; set; }
    public string? OutputTail { get; set; }
    public bool CancelRequested { get; set; }
    public string? RequestedBy { get; set; }

    public bool IsActive => Status is BackupJobStatus.Queued or BackupJobStatus.Dispatched or BackupJobStatus.Running;

    public void Configure(EntityTypeBuilder<BackupJob> builder)
    {
        builder.Property(j => j.VolumeKey).HasMaxLength(320);
        builder.Property(j => j.VolumeName).HasMaxLength(255);
        builder.Property(j => j.NodeName).HasMaxLength(200);
        builder.Property(j => j.RepositoryKey).HasMaxLength(64);
        builder.Property(j => j.StackName).HasMaxLength(200);
        builder.Property(j => j.SnapshotId).HasMaxLength(64);
        builder.Property(j => j.RequestedBy).HasMaxLength(256);
        builder.HasIndex(j => j.Status);
        builder.HasIndex(j => new { j.VolumeKey, j.QueuedAt });
        builder.HasIndex(j => j.ParentJobId);
        builder.HasIndex(j => j.QueuedAt);
    }
}
