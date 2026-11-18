using System.Text.Json;
using System.Text.Json.Serialization;
using Track.Enums;
using Track.Services.Backups;

namespace Track.Website.Services.Backups;

/// <summary>What a job needs beyond its columns, stored as JSON on the job. Secrets are never stored here.</summary>
public sealed record JobParameters
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string? Host { get; init; }
    public List<string>? Paths { get; init; }
    public List<string>? Tags { get; init; }
    public List<AgentHook>? PreHooks { get; init; }
    public List<AgentHook>? PostHooks { get; init; }
    public List<AgentHook>? Hooks { get; init; }
    public List<string>? Images { get; init; }
    public string? SnapshotId { get; init; }
    public string? SnapshotPath { get; init; }
    public string? RestoreVolume { get; init; }
    public bool RestoreDelete { get; init; }
    public bool StopContainers { get; init; }
    public List<ServiceReplicas>? Scale { get; init; }

    /// <summary>For a Replace parent: the steps, run in order, each as a child job.</summary>
    public List<ReplaceStep>? Steps { get; init; }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static JobParameters Parse(string? json) =>
        string.IsNullOrEmpty(json) ? new JobParameters() : JsonSerializer.Deserialize<JobParameters>(json, JsonOptions) ?? new JobParameters();
}

public sealed record ReplaceStep(
    string Name, BackupJobKind Kind, string? NodeName, string? RepositoryKey, JobParameters Parameters, bool ScalesDown = false);
