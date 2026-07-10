namespace Track.Website.Api.v0.Models.Stats;

public class UsageTotalsModel
{
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryLimitBytes { get; set; }

    /// <summary>Container writable layers plus stack-labelled volumes.</summary>
    public long DiskBytes { get; set; }

    /// <summary>Consumed CPU as a share of total cluster cores (0-100).</summary>
    public double CpuPercentOfCluster { get; set; }

    /// <summary>Consumed memory as a share of total cluster memory (0-100).</summary>
    public double MemoryPercentOfCluster { get; set; }

    /// <summary>Mean of CPU and memory cluster share, the single blended billing figure (0-100).</summary>
    public double BlendedPercentOfCluster { get; set; }
}
