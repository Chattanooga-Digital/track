namespace Track.Website.Api.v0.Models.Stats;

public class ContainerStatsModel
{
    public required string NodeName { get; set; }
    public required string ContainerId { get; set; }
    public required string ContainerName { get; set; }
    public string? StackName { get; set; }
    public string? ServiceName { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryLimitBytes { get; set; }

    /// <summary>Writable-layer size. Null until the first disk poll completes.</summary>
    public long? DiskBytes { get; set; }

    public DateTime Timestamp { get; set; }
}
