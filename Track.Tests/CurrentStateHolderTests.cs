using Track.Services;

namespace Track.Tests;

public class CurrentStateHolderTests
{
    private static BucketUsage Bucket(string source, string name, long size, long count = 0) =>
        new(source, name, size, count);

    [Test]
    public void GetCurrent_ExposesBucketsFromAllSources()
    {
        var holder = new CurrentStateHolder();
        holder.SetBucketUsage("minio-a", [Bucket("minio-a", "nextcloud", 4_700), Bucket("minio-a", "mastodon", 317)]);
        holder.SetBucketUsage("minio-b", [Bucket("minio-b", "backups", 1_000)]);

        var buckets = holder.GetCurrent().Buckets;

        Assert.That(buckets, Has.Count.EqualTo(3));
        Assert.That(buckets.Single(b => b.SourceName == "minio-a" && b.BucketName == "nextcloud").SizeBytes,
            Is.EqualTo(4_700));
        Assert.That(buckets.Single(b => b.SourceName == "minio-b" && b.BucketName == "backups").SizeBytes,
            Is.EqualTo(1_000));
    }

    [Test]
    public void SetBucketUsage_ReplacesPreviousReadingForTheSameSource()
    {
        var holder = new CurrentStateHolder();
        holder.SetBucketUsage("minio-a", [Bucket("minio-a", "nextcloud", 4_700)]);
        holder.SetBucketUsage("minio-a", [Bucket("minio-a", "nextcloud", 5_100)]);

        var buckets = holder.GetCurrent().Buckets;

        Assert.That(buckets, Has.Count.EqualTo(1));
        Assert.That(buckets[0].SizeBytes, Is.EqualTo(5_100));
    }

    [Test]
    public void GetCurrent_KeepsSameBucketNameDistinctAcrossSources()
    {
        var holder = new CurrentStateHolder();
        holder.SetBucketUsage("minio-a", [Bucket("minio-a", "shared", 10)]);
        holder.SetBucketUsage("minio-b", [Bucket("minio-b", "shared", 20)]);

        var buckets = holder.GetCurrent().Buckets;

        // A bucket named "shared" on two sources is two distinct buckets, not a collision.
        Assert.That(buckets, Has.Count.EqualTo(2));
        Assert.That(buckets.Sum(b => b.SizeBytes), Is.EqualTo(30));
    }

    [Test]
    public void RetainBucketSources_DropsBucketsFromSourcesNoLongerPresent()
    {
        var holder = new CurrentStateHolder();
        holder.SetBucketUsage("minio-a", [Bucket("minio-a", "nextcloud", 4_700)]);
        holder.SetBucketUsage("minio-b", [Bucket("minio-b", "backups", 1_000)]);

        holder.RetainBucketSources(new HashSet<string> { "minio-a" });

        var buckets = holder.GetCurrent().Buckets;
        Assert.That(buckets.Select(b => b.SourceName), Is.EquivalentTo(new[] { "minio-a" }));
    }
}
