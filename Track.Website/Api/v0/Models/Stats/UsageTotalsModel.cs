namespace Track.Website.Api.v0.Models.Stats;

public class UsageTotalsModel
{
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryLimitBytes { get; set; }

    /// <summary>Container writable layers plus stack-labelled volumes (node-local disk).</summary>
    public long DiskBytes { get; set; }

    /// <summary>S3 bucket storage assigned to this scope. Kept separate from node-local <see cref="DiskBytes"/>.</summary>
    public long ObjectStorageBytes { get; set; }

    /// <summary>Consumed CPU as a share of total cluster cores (0-100).</summary>
    public double CpuPercentOfCluster { get; set; }

    /// <summary>Consumed memory as a share of total cluster memory (0-100).</summary>
    public double MemoryPercentOfCluster { get; set; }

    /// <summary>Mean of CPU and memory cluster share, the single blended billing figure (0-100).</summary>
    public double BlendedPercentOfCluster { get; set; }

    /// <summary>Compute charge: this scope's share of the recovered cluster cost. $0 for the unassigned group.</summary>
    public decimal ComputeCost { get; set; }

    /// <summary>Node-local disk charge at the flat per-GB rate.</summary>
    public decimal DiskCost { get; set; }

    /// <summary>Object storage charge at the flat per-GB rate.</summary>
    public decimal ObjectStorageCost { get; set; }

    /// <summary>Compute + disk + object storage.</summary>
    public decimal TotalCost { get; set; }
}
