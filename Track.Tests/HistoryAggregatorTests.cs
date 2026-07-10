using Track.Website.Queries;

namespace Track.Tests;

public class HistoryAggregatorTests
{
    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Aggregate_AveragesSummedUsageAcrossPollRounds()
    {
        // Two poll rounds inside one bucket, two containers each: totals 30% and 50% CPU.
        List<HistoryAggregator.UsagePoint> usage =
        [
            new(From.AddSeconds(0), 10, 100, 1000),
            new(From.AddSeconds(0), 20, 100, 1000),
            new(From.AddSeconds(15), 25, 300, 1000),
            new(From.AddSeconds(15), 25, 100, 1000),
        ];

        var buckets = HistoryAggregator.Aggregate(usage, [], From, From.AddHours(1));

        Assert.That(buckets, Has.Count.EqualTo(1));
        Assert.That(buckets[0].CpuPercent, Is.EqualTo(40.0).Within(0.001));
        Assert.That(buckets[0].MemoryUsedBytes, Is.EqualTo(300));
        Assert.That(buckets[0].MemoryLimitBytes, Is.EqualTo(2000));
    }

    [Test]
    public void Aggregate_SplitsSamplesIntoSeparateBuckets()
    {
        List<HistoryAggregator.UsagePoint> usage =
        [
            new(From.AddMinutes(0), 10, 100, 1000),
            new(From.AddMinutes(30), 30, 100, 1000),
        ];

        var buckets = HistoryAggregator.Aggregate(usage, [], From, From.AddHours(1), targetBuckets: 2);

        Assert.That(buckets, Has.Count.EqualTo(2));
        Assert.That(buckets[0].CpuPercent, Is.EqualTo(10.0).Within(0.001));
        Assert.That(buckets[1].CpuPercent, Is.EqualTo(30.0).Within(0.001));
        Assert.That(buckets[1].BucketStart, Is.EqualTo(From.AddMinutes(30)));
    }

    [Test]
    public void Aggregate_IncludesDiskOnlyBuckets()
    {
        List<HistoryAggregator.DiskPoint> disk =
        [
            new(From.AddMinutes(1), 5000),
            new(From.AddMinutes(1), 3000),
        ];

        var buckets = HistoryAggregator.Aggregate([], disk, From, From.AddHours(1));

        Assert.That(buckets, Has.Count.EqualTo(1));
        Assert.That(buckets[0].DiskBytes, Is.EqualTo(8000));
        Assert.That(buckets[0].CpuPercent, Is.EqualTo(0));
    }
}
