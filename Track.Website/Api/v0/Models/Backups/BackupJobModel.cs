using Track.Enums;

namespace Track.Website.Api.v0.Models.Backups;

public class BackupJobModel
{
    public long Id { get; set; }
    public BackupJobKind Kind { get; set; }
    public BackupJobStatus Status { get; set; }
    public BackupTrigger Trigger { get; set; }
    public string? VolumeKey { get; set; }
    public string? VolumeName { get; set; }
    public string? NodeName { get; set; }
    public string? RepositoryKey { get; set; }
    public long? ParentJobId { get; set; }
    public string? StepName { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public double? PercentDone { get; set; }
    public long? BytesDone { get; set; }
    public long? BytesTotal { get; set; }
    public long? BytesAdded { get; set; }
    public long? FilesNew { get; set; }
    public long? FilesChanged { get; set; }
    public string? SnapshotId { get; set; }
    public string? Error { get; set; }
    public string? Summary { get; set; }
    public Guid? LogSessionId { get; set; }
    public string? RequestedBy { get; set; }
    public bool CancelRequested { get; set; }
    public string? Output { get; set; }
}
