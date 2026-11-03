using Track.Data.Models;

namespace Track.Services.Ingest;

/// <summary>
/// One agent's stats push: its node capacity, a sample per running container, and an inventory of the
/// label-less standalone containers on the node. Reuses the storage entities as the wire format;
/// <see cref="ContainerSample.Id"/> is 0 until the central instance persists it.
/// </summary>
public record StatsReport(
    string NodeName,
    DateTime Timestamp,
    NodeCapacity Capacity,
    List<ContainerSample> Samples,
    List<LabelLessContainer> Inventory);

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
/// grouping identities, and the central's build version, which standalone agents update themselves to.
/// </summary>
public record IngestResponse(List<string> IdentityEnvVars, string? Version = null);
