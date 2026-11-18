using Track.Enums;

namespace Track.Services.Backups;

/// <summary>Everything restic needs to reach one repository. Handed to an agent only with a job for that repository.</summary>
public sealed record ResticTarget(
    string Repository, string Password, string AccessKey, string SecretKey, string Region, S3Addressing Addressing)
{
    public List<string> Options =>
    [
        "-o", $"s3.bucket-lookup={Addressing switch { S3Addressing.VirtualHost => "dns", S3Addressing.Path => "path", _ => "auto" }}",
        "-o", $"s3.region={Region}",
    ];

    public Dictionary<string, string> Environment => new()
    {
        ["RESTIC_REPOSITORY"] = Repository,
        ["RESTIC_PASSWORD"] = Password,
        ["AWS_ACCESS_KEY_ID"] = AccessKey,
        ["AWS_SECRET_ACCESS_KEY"] = SecretKey,
    };
}

public sealed record AgentHook(string ServiceName, string Command);

public sealed record ServiceReplicas(string ServiceName, ulong Replicas);

/// <summary>One job as an agent runs it. Which fields matter depends on <see cref="Kind"/>.</summary>
public sealed record AgentJob(
    long Id,
    BackupJobKind Kind,
    string? VolumeName,
    string? Host,
    ResticTarget? Restic,
    List<string> Paths,
    List<string> Tags,
    List<AgentHook> PreHooks,
    List<AgentHook> PostHooks,
    List<AgentHook> Hooks,
    string? SnapshotId,
    string? SnapshotPath,
    string? RestoreVolume,
    bool RestoreDelete,
    bool StopContainers,
    List<ServiceReplicas> Scale,
    int HookTimeoutSeconds);

public sealed record AgentJobProgress(double? PercentDone, long? BytesDone, long? BytesTotal);

public sealed record AgentJobProgressResponse(bool Cancel);

public sealed record AgentJobResult(
    bool Succeeded,
    bool Cancelled,
    string? Error,
    string? SnapshotId,
    long? BytesAdded,
    long? FilesNew,
    long? FilesChanged,
    long? TotalBytes,
    string? Output);
