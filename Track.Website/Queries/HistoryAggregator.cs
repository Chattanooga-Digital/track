using Track.Website.Api.v0.Models.Stats;

namespace Track.Website.Queries;

public static class HistoryAggregator
{
    public record UsagePoint(DateTime Timestamp, double CpuPercent, long MemoryUsedBytes, long MemoryLimitBytes);

    public record DiskPoint(DateTime Timestamp, long SizeBytes);

    /// <summary>
    /// Buckets raw samples into at most <paramref name="targetBuckets"/> time buckets. Within a bucket,
    /// samples are summed per poll round (all containers polled at the same timestamp) and averaged
    /// across rounds, so the result is the average total usage regardless of container count.
    /// </summary>
    public static List<HistoryBucketModel> Aggregate(
        IReadOnlyList<UsagePoint> usagePoints,
        IReadOnlyList<DiskPoint> diskPoints,
        IReadOnlyList<DiskPoint> objectStoragePoints,
        DateTime from,
        DateTime to,
        int targetBuckets = 200)
    {
        var totalSeconds = Math.Max(1, (to - from).TotalSeconds);
        var bucketSeconds = Math.Max(60, (long)Math.Ceiling(totalSeconds / targetBuckets));
        var buckets = new SortedDictionary<long, HistoryBucketModel>();

        foreach (var group in usagePoints.GroupBy(p => BucketIndex(p.Timestamp, from, bucketSeconds)))
        {
            var pollRounds = group.Select(p => p.Timestamp).Distinct().Count();
            buckets[group.Key] = new HistoryBucketModel
            {
                BucketStart = BucketStart(group.Key, from, bucketSeconds),
                CpuPercent = group.Sum(p => p.CpuPercent) / pollRounds,
                MemoryUsedBytes = (long)(group.Sum(p => (double)p.MemoryUsedBytes) / pollRounds),
                MemoryLimitBytes = (long)(group.Sum(p => (double)p.MemoryLimitBytes) / pollRounds),
            };
        }

        foreach (var group in diskPoints.GroupBy(p => BucketIndex(p.Timestamp, from, bucketSeconds)))
        {
            var pollRounds = group.Select(p => p.Timestamp).Distinct().Count();
            var diskBytes = (long)(group.Sum(p => (double)p.SizeBytes) / pollRounds);

            if (buckets.TryGetValue(group.Key, out var bucket))
                bucket.DiskBytes = diskBytes;
            else
                buckets[group.Key] = new HistoryBucketModel
                {
                    BucketStart = BucketStart(group.Key, from, bucketSeconds),
                    DiskBytes = diskBytes,
                };
        }

        foreach (var group in objectStoragePoints.GroupBy(p => BucketIndex(p.Timestamp, from, bucketSeconds)))
        {
            var pollRounds = group.Select(p => p.Timestamp).Distinct().Count();
            var objectStorageBytes = (long)(group.Sum(p => (double)p.SizeBytes) / pollRounds);

            if (buckets.TryGetValue(group.Key, out var bucket))
                bucket.ObjectStorageBytes = objectStorageBytes;
            else
                buckets[group.Key] = new HistoryBucketModel
                {
                    BucketStart = BucketStart(group.Key, from, bucketSeconds),
                    ObjectStorageBytes = objectStorageBytes,
                };
        }

        return [.. buckets.Values];
    }

    private static long BucketIndex(DateTime timestamp, DateTime from, long bucketSeconds) =>
        (long)(timestamp - from).TotalSeconds / bucketSeconds;

    private static DateTime BucketStart(long index, DateTime from, long bucketSeconds) =>
        from.AddSeconds(index * bucketSeconds);
}
