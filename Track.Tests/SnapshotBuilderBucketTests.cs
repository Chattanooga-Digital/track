using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services;
using Track.Website.Queries;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Track.Tests;

public class SnapshotBuilderBucketTests
{
    private static DiskSample Bucket(string source, string name, long size) => new()
    {
        Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        NodeName = source,
        Kind = DiskUsageKind.Bucket,
        Name = name,
        SizeBytes = size,
    };

    private static async Task<(TrackContext Context, SqliteConnection Connection)> NewContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TrackContext>().UseSqlite(connection).Options;
        var context = new TrackContext(options);
        await context.Database.EnsureCreatedAsync();
        return (context, connection);
    }

    [Test]
    public async Task BuildAsync_PlacesAssignedBucketUnderItsCustomerAndRollsUpObjectStorage()
    {
        var (context, connection) = await NewContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var customer = new Customer { Name = "Acme", CreatedAt = DateTime.UtcNow };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        context.BucketAssignments.Add(new BucketAssignment
        {
            SourceName = "minio-a",
            BucketName = "nextcloud",
            CustomerId = customer.Id,
        });
        await context.SaveChangesAsync();

        var holder = new CurrentStateHolder();
        holder.SetBucketUsage("minio-a", [Bucket("minio-a", "nextcloud", 4_700), Bucket("minio-a", "orphan", 1_000)]);

        var snapshot = await new SnapshotBuilder(context, holder).BuildAsync();

        var acme = snapshot.Customers.Single(c => c.CustomerId == customer.Id);
        Assert.That(acme.Buckets.Select(b => b.BucketName), Is.EquivalentTo(new[] { "nextcloud" }));
        Assert.That(acme.Totals.ObjectStorageBytes, Is.EqualTo(4_700));

        // The unassigned bucket lands in the catch-all group, not under the customer.
        var unassigned = snapshot.Customers.Single(c => c.CustomerId is null);
        Assert.That(unassigned.Buckets.Select(b => b.BucketName), Is.EquivalentTo(new[] { "orphan" }));
        Assert.That(snapshot.Totals.ObjectStorageBytes, Is.EqualTo(5_700));
    }

    [Test]
    public async Task BuildAsync_KeepsObjectStorageSeparateFromNodeLocalDisk()
    {
        var (context, connection) = await NewContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var holder = new CurrentStateHolder();
        holder.SetBucketUsage("minio-a", [Bucket("minio-a", "nextcloud", 4_700)]);

        var snapshot = await new SnapshotBuilder(context, holder).BuildAsync();

        // No containers or volumes were reported, so node-local disk stays zero while object storage is counted.
        Assert.That(snapshot.Totals.DiskBytes, Is.EqualTo(0));
        Assert.That(snapshot.Totals.ObjectStorageBytes, Is.EqualTo(4_700));
    }
}
