using Track.Enums;
using Track.Services.Backups;

namespace Track.Website.Api.v0.Models.Backups;

public class BackupVolumeModel
{
    public required string Key { get; set; }
    public required string VolumeName { get; set; }
    public string? NodeName { get; set; }
    public string? StackName { get; set; }
    public List<string> Services { get; set; } = [];
    public List<string> Images { get; set; } = [];
    public List<string> Copies { get; set; } = [];
    public long? SizeBytes { get; set; }
    public BackupVolumeStatus Status { get; set; }
    public ExclusionSource? ExcludedBy { get; set; }

    /// <summary>True or false when Track's choice overrides the labels; null when the labels decide.</summary>
    public bool? Override { get; set; }

    public bool HasHooks { get; set; }
    public bool HasRestoreHooks { get; set; }
    public bool DatabaseWithoutHook { get; set; }
    public List<string> Paths { get; set; } = [];
    public DateTime? LastGoodAt { get; set; }
    public long? ActiveJobId { get; set; }
    public BackupJobKind? ActiveJobKind { get; set; }
    public double? PercentDone { get; set; }
    public string? LastError { get; set; }
    public string? ReplaceBlockedReason { get; set; }
}
