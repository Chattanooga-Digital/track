using Track.Data.Models;
using Track.Enums;

namespace Track.Services.Ingest;

/// <summary>
/// One agent's stats push: its node capacity, a sample per running container, an inventory of the
/// label-less standalone containers on the node, and the agent's build version. Reuses the storage
/// entities as the wire format; <see cref="ContainerSample.Id"/> is 0 until the central instance persists it.
/// </summary>
public record StatsReport(
    string NodeName,
    DateTime Timestamp,
    NodeCapacity Capacity,
    List<ContainerSample> Samples,
    List<LabelLessContainer> Inventory,
    string? AgentVersion = null,
    NodeRole Role = NodeRole.Standalone,
    List<VolumeMount>? Mounts = null);

/// <summary>A volume mounted by a running container, with the container's <c>backupbot.*</c> labels and nothing else.</summary>
public record VolumeMount(
    string VolumeName,
    string ContainerId,
    string ContainerName,
    string? StackName,
    string? ServiceName,
    string? Image,
    Dictionary<string, string> BackupLabels,
    string? Destination = null);

/// <summary>A manager agent's view of the swarm: what only a manager's engine can answer.</summary>
public record SwarmReport(string NodeName, DateTime Timestamp, List<SwarmNodeInfo> Nodes, List<SwarmServiceInfo> Services);

public record SwarmNodeInfo(string Hostname, string Role, string Availability, string State, Dictionary<string, string> Labels);

public record SwarmServiceInfo(
    string Name,
    string? StackName,
    string? Image,
    bool Global,
    ulong? Replicas,
    List<string> Volumes,
    List<string> Constraints,
    Dictionary<string, string> BackupLabels,
    List<SwarmTaskInfo> RunningTasks);

public record SwarmTaskInfo(string NodeHostname, string? ContainerId);

/// <summary>One agent's disk-usage push: per-container writable-layer sizes and per-volume sizes.</summary>
public record DiskReport(string NodeName, DateTime Timestamp, List<DiskSample> Samples);

/// <summary>
/// A standalone container the agent could not group by a stack/compose label. Carries only its name,
/// image, and env var <b>key names</b> so the central UI can offer them as grouping identities. Env
/// values are never included - they may hold secrets.
/// </summary>
public record LabelLessContainer(string ContainerId, string ContainerName, string? Image, List<string> EnvKeys);

/// <summary>
/// The central's response to a stats push: the authoritative ordered list of env var names to use as
/// grouping identities, the central's build version (which standalone agents update themselves to), the
/// agent's approval, and, once approved, the backup jobs waiting for it.
/// </summary>
public record IngestResponse(
    List<string> IdentityEnvVars,
    string? Version = null,
    AgentApproval? Approval = null,
    List<long>? BackupJobIds = null);
