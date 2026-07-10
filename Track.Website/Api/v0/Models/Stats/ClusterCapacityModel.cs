namespace Track.Website.Api.v0.Models.Stats;

public class ClusterCapacityModel
{
    public int NodeCount { get; set; }
    public long CpuCores { get; set; }
    public long MemoryBytes { get; set; }
}
