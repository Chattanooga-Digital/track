namespace Track.Website.Api.v0.Models.Stats;

public class HistoryBucketModel
{
    public DateTime BucketStart { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryLimitBytes { get; set; }

    /// <summary>Null for buckets with no disk samples (disk is polled less often than stats).</summary>
    public long? DiskBytes { get; set; }

    /// <summary>S3 bucket storage. Null for time buckets with no bucket samples.</summary>
    public long? ObjectStorageBytes { get; set; }
}
