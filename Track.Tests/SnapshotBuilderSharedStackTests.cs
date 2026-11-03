using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Website.Api.v0.Controllers;
using Track.Website.Api.v0.Models.Stacks;
using Track.Website.Queries;
using Track.Website.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Track.Tests;

public class SnapshotBuilderSharedStackTests
{
    private static async Task<(TrackContext Context, SqliteConnection Connection)> NewContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TrackContext>().UseSqlite(connection).Options;
        var context = new TrackContext(options);
        await context.Database.EnsureCreatedAsync();
        return (context, connection);
    }

    private static ContainerSample Container(string stack, double cpuPercent, long memoryBytes) => new()
    {
        NodeName = "node1",
        ContainerId = stack,
        ContainerName = $"{stack}.1",
        StackName = stack,
        CpuPercent = cpuPercent,
        MemoryUsedBytes = memoryBytes,
        MemoryLimitBytes = 8_000_000_000,
        Timestamp = DateTime.UtcNow,
    };

    private static async Task<(TrackContext Context, SqliteConnection Connection, CurrentStateHolder Holder)> SeedAsync()
    {
        var (context, connection) = await NewContextAsync();

        var customer = new Customer { Name = "Acme", CreatedAt = DateTime.UtcNow };
        context.Customers.Add(customer);
        context.BillingRates.Add(new BillingRate { ClusterComputeMonthlyCost = 100m });
        await context.SaveChangesAsync();
        context.StackAssignments.Add(new StackAssignment { StackName = "web", CustomerId = customer.Id });
        context.SharedStacks.Add(new SharedStack { StackName = "traefik" });
        context.SharedBuckets.Add(new SharedBucket { SourceName = "minio", BucketName = "backups" });
        await context.SaveChangesAsync();

        var holder = new CurrentStateHolder();
        holder.SetNodeSamples("node1", new NodeCollection(
            new NodeCapacity("node1", CpuCores: 4, MemoryBytes: 8_000_000_000),
            [
                Container("web", 100, 2_000_000_000),
                Container("traefik", 40, 800_000_000),
                Container("forgotten", 20, 400_000_000),
            ],
            []), DateTime.UtcNow);
        holder.SetBucketUsage("minio", [new BucketUsage("minio", "backups", 9_000, 0), new BucketUsage("minio", "orphan", 1_000, 0)]);

        return (context, connection, holder);
    }

    [Test]
    public async Task BuildAsync_GroupsSharedStacksApartFromUnassignedAndBillsNeither()
    {
        var (context, connection, holder) = await SeedAsync();
        await using var _ = connection;
        await using var __ = context;

        var snapshot = await new SnapshotBuilder(context, holder).BuildAsync();

        var shared = snapshot.Customers.Single(c => c.IsShared);
        Assert.That(shared.CustomerId, Is.Null);
        Assert.That(shared.Stacks.Select(s => s.StackName), Is.EquivalentTo(new[] { "traefik" }));
        Assert.That(shared.Buckets.Select(b => b.BucketName), Is.EquivalentTo(new[] { "backups" }));
        Assert.That(shared.Totals.ObjectStorageCost, Is.EqualTo(0m));
        Assert.That(shared.Totals.BlendedPercentOfCluster, Is.GreaterThan(0));
        Assert.That(shared.Totals.TotalCost, Is.EqualTo(0m));

        var unassigned = snapshot.Customers.Single(c => c.CustomerId is null && !c.IsShared);
        Assert.That(unassigned.Stacks.Select(s => s.StackName), Is.EquivalentTo(new[] { "forgotten" }));
        Assert.That(unassigned.Buckets.Select(b => b.BucketName), Is.EquivalentTo(new[] { "orphan" }));
        Assert.That(unassigned.Totals.TotalCost, Is.EqualTo(0m));

        // The only paying customer still recovers the whole cluster cost.
        var acme = snapshot.Customers.Single(c => c.CustomerId is not null);
        Assert.That(acme.Totals.ComputeCost, Is.EqualTo(100m));
        Assert.That(snapshot.Totals.ComputeCost, Is.EqualTo(100m));
    }

    [Test]
    public async Task SetAssignment_MovesAStackBetweenCustomerSharedAndUnassigned()
    {
        var (context, connection, holder) = await SeedAsync();
        await using var _ = connection;
        await using var __ = context;

        var controller = new StacksController(NullLogger<StacksController>.Instance, context, holder);
        var customerId = await context.Customers.Select(c => c.Id).SingleAsync();

        await controller.SetAssignment("web", new StackAssignmentEditModel { IsShared = true }, CancellationToken.None);
        Assert.That(await context.StackAssignments.AnyAsync(a => a.StackName == "web"), Is.False);
        Assert.That(await context.SharedStacks.AnyAsync(s => s.StackName == "web"), Is.True);

        await controller.SetAssignment("web", new StackAssignmentEditModel { CustomerId = customerId }, CancellationToken.None);
        Assert.That(await context.SharedStacks.AnyAsync(s => s.StackName == "web"), Is.False);
        Assert.That(await context.StackAssignments.SingleAsync(a => a.StackName == "web"), Has.Property("CustomerId").EqualTo(customerId));

        await controller.SetAssignment("web", new StackAssignmentEditModel(), CancellationToken.None);
        Assert.That(await context.StackAssignments.AnyAsync(a => a.StackName == "web"), Is.False);
        Assert.That(await context.SharedStacks.AnyAsync(s => s.StackName == "web"), Is.False);
    }

    [Test]
    public async Task RecordAsync_MarksTheSharedGroupsBillingSample()
    {
        var (context, connection, holder) = await SeedAsync();
        await using var _ = connection;
        await using var __ = context;

        await new BillingSampleRecorder(context, new SnapshotBuilder(context, holder)).RecordAsync();

        var unbilled = await context.BillingSamples.Where(s => s.CustomerId == null).ToListAsync();
        Assert.That(unbilled.Count(s => s.IsShared), Is.EqualTo(1));
        Assert.That(unbilled.Count(s => !s.IsShared), Is.EqualTo(1));
    }
}
