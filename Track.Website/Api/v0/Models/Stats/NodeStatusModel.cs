namespace Track.Website.Api.v0.Models.Stats;

public class NodeStatusModel
{
    public required string Name { get; set; }
    public bool Online { get; set; }
    public string? Error { get; set; }
    public DateTime? LastSeen { get; set; }

    /// <summary>The agent's build version, e.g. 0.9.1.24; null for agents too old to report it.</summary>
    public string? AgentVersion { get; set; }

    /// <summary>Running containers this agent currently reports.</summary>
    public int ContainersTracked { get; set; }

    /// <summary>Of those, label-less containers not yet grouped by a stack or identity env var.</summary>
    public int UnidentifiedContainers { get; set; }

    public long CpuCores { get; set; }
    public long MemoryBytes { get; set; }

    /// <summary>Live summed CPU (per-core, 100 = one core) and memory across the agent's containers.</summary>
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
}
