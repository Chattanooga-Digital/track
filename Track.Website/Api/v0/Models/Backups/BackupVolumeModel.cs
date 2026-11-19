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
    public bool DatabaseWithoutDump { get; set; }
    public BackupMethod Method { get; set; }

    /// <summary>Track's choice of method; null when the labels and the detected database decide.</summary>
    public BackupMethod? MethodOverride { get; set; }

    /// <summary>The database Track can dump from this volume, if any.</summary>
    public DatabaseEngine? Engine { get; set; }

    public BackupTestModel? LastTest { get; set; }
    public List<string> Paths { get; set; } = [];
    public DateTime? LastGoodAt { get; set; }
    public long? ActiveJobId { get; set; }
    public BackupJobKind? ActiveJobKind { get; set; }
    public double? PercentDone { get; set; }
    public string? LastError { get; set; }
    public string? ReplaceBlockedReason { get; set; }
    public string? DumpReplaceBlockedReason { get; set; }
}

public class BackupTestModel
{
    public long JobId { get; set; }
    public DateTime? FinishedAt { get; set; }
    public bool Succeeded { get; set; }

    /// <summary>What arrived, or why the test failed.</summary>
    public string? Message { get; set; }
}
