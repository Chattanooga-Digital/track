using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Website.Api.v0.Controllers;
using Track.Website.Api.v0.Models.Stats;
using Track.Website.Queries;
using Track.Website.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Track.Tests;

public class BillingTests
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

    private static StatsController NewController(TrackContext context) =>
        new(NullLogger<StatsController>.Instance, context, new SnapshotBuilder(context, new CurrentStateHolder()));

    [Test]
    public async Task RecordAsync_WritesOneCapacityRowAndOneRowPerCustomer()
    {
        var (context, connection) = await NewContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var customer = new Customer { Name = "Acme", CreatedAt = DateTime.UtcNow };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        context.StackAssignments.Add(new StackAssignment { StackName = "web", CustomerId = customer.Id });
        await context.SaveChangesAsync();

        var holder = new CurrentStateHolder();
        holder.SetNodeSamples("node1", new NodeCollection(
            new NodeCapacity("node1", CpuCores: 4, MemoryBytes: 8_000_000_000),
            [
                new ContainerSample
                {
                    NodeName = "node1",
                    ContainerId = "c1",
                    ContainerName = "web.1",
                    StackName = "web",
                    CpuPercent = 100,
                    MemoryUsedBytes = 2_000_000_000,
                    MemoryLimitBytes = 8_000_000_000,
                    Timestamp = DateTime.UtcNow,
                },
            ],
            []), DateTime.UtcNow);

        var recorded = await new BillingSampleRecorder(context, new SnapshotBuilder(context, holder)).RecordAsync();

        Assert.That(recorded, Is.True);

        var capacity = await context.ClusterCapacitySamples.SingleAsync();
        Assert.That(capacity.NodeCount, Is.EqualTo(1));
        Assert.That(capacity.CpuCores, Is.EqualTo(4));

        var sample = await context.BillingSamples.SingleAsync();
        Assert.That(sample.CustomerId, Is.EqualTo(customer.Id));
        Assert.That(sample.CustomerName, Is.EqualTo("Acme"));
        // 100% of one core on a 4-core cluster = 25%; 2 GB of 8 GB = 25%; blended = 25%.
        Assert.That(sample.CpuPercentOfCluster, Is.EqualTo(25).Within(0.001));
        Assert.That(sample.MemoryPercentOfCluster, Is.EqualTo(25).Within(0.001));
        Assert.That(sample.BlendedPercentOfCluster, Is.EqualTo(25).Within(0.001));
    }

    [Test]
    public async Task RecordAsync_SkipsWhenNoNodesAreOnline()
    {
        var (context, connection) = await NewContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var recorded = await new BillingSampleRecorder(context, new SnapshotBuilder(context, new CurrentStateHolder()))
            .RecordAsync();

        Assert.That(recorded, Is.False);
        Assert.That(await context.ClusterCapacitySamples.AnyAsync(), Is.False);
        Assert.That(await context.BillingSamples.AnyAsync(), Is.False);
    }

    [Test]
    public async Task GetBillingMonth_AveragesOverTheMonthsTicks()
    {
        var (context, connection) = await NewContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var tick1 = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var tick2 = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        context.ClusterCapacitySamples.AddRange(
            new ClusterCapacitySample { Timestamp = tick1, NodeCount = 2, CpuCores = 8, MemoryBytes = 16_000_000_000 },
            new ClusterCapacitySample { Timestamp = tick2, NodeCount = 2, CpuCores = 8, MemoryBytes = 16_000_000_000 });
        context.BillingSamples.AddRange(
            // Customer 1 present at both ticks; customer 2 only at the second.
            new BillingSample { Timestamp = tick1, CustomerId = 1, CustomerName = "Acme", BlendedPercentOfCluster = 10, MemoryUsedBytes = 1_000 },
            new BillingSample { Timestamp = tick2, CustomerId = 1, CustomerName = "Acme", BlendedPercentOfCluster = 20, MemoryUsedBytes = 3_000 },
            new BillingSample { Timestamp = tick2, CustomerId = 2, CustomerName = "Globex", BlendedPercentOfCluster = 30 },
            // Outside the requested month; must not leak in.
            new BillingSample { Timestamp = tick1.AddMonths(-1), CustomerId = 1, CustomerName = "Acme", BlendedPercentOfCluster = 99 });
        context.ClusterCapacitySamples.Add(new ClusterCapacitySample
        {
            Timestamp = tick1.AddMonths(-1),
            NodeCount = 1,
            CpuCores = 4,
            MemoryBytes = 8_000_000_000,
        });
        await context.SaveChangesAsync();

        var result = await NewController(context).GetBillingMonth("2026-06", default) as OkObjectResult;
        var model = (BillingMonthModel)result!.Value!;

        Assert.That(model.SampleCount, Is.EqualTo(2));
        Assert.That(model.Capacity.CpuCores, Is.EqualTo(8));

        var acme = model.Customers.Single(c => c.CustomerId == 1);
        Assert.That(acme.CustomerName, Is.EqualTo("Acme"));
        Assert.That(acme.BlendedPercentOfCluster, Is.EqualTo(15).Within(0.001));
        Assert.That(acme.MemoryUsedBytes, Is.EqualTo(2_000).Within(0.001));

        // Present for one of two ticks, so the month average is half the while-present figure.
        var globex = model.Customers.Single(c => c.CustomerId == 2);
        Assert.That(globex.BlendedPercentOfCluster, Is.EqualTo(15).Within(0.001));
    }

    [Test]
    public async Task GetBillingMonth_RecoversFullClusterCostFromCustomers_UnassignedBillsNothing()
    {
        var (context, connection) = await NewContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var tick = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        context.ClusterCapacitySamples.Add(new ClusterCapacitySample
        {
            Timestamp = tick,
            NodeCount = 1,
            CpuCores = 4,
            MemoryBytes = 8_000_000_000,
        });
        context.BillingSamples.AddRange(
            new BillingSample { Timestamp = tick, CustomerId = 1, CustomerName = "Acme", BlendedPercentOfCluster = 10 },
            new BillingSample { Timestamp = tick, CustomerId = 2, CustomerName = "Globex", BlendedPercentOfCluster = 30 },
            // The unassigned remainder (null customer) consumes too, but must never be billed.
            new BillingSample { Timestamp = tick, CustomerId = null, CustomerName = null, BlendedPercentOfCluster = 60 });
        context.BillingRates.Add(new BillingRate { ClusterComputeMonthlyCost = 1000m, Currency = "USD" });
        await context.SaveChangesAsync();

        var result = await NewController(context).GetBillingMonth("2026-06", default) as OkObjectResult;
        var model = (BillingMonthModel)result!.Value!;

        // Base rate spreads $1000 over the 40 paying blended points (unassigned's 60 excluded).
        Assert.That(model.Customers.Single(c => c.CustomerId == 1).ComputeCost, Is.EqualTo(250m));
        Assert.That(model.Customers.Single(c => c.CustomerId == 2).ComputeCost, Is.EqualTo(750m));
        Assert.That(model.Customers.Single(c => c.CustomerId == null).ComputeCost, Is.EqualTo(0m));
        Assert.That(model.ComputeCost, Is.EqualTo(1000m)); // full recovery from paying customers
        Assert.That(model.Currency, Is.EqualTo("USD"));
    }

    [Test]
    public async Task GetBillingMonths_ListsMonthsNewestFirst()
    {
        var (context, connection) = await NewContextAsync();
        await using var _ = connection;
        await using var __ = context;

        context.ClusterCapacitySamples.AddRange(
            new ClusterCapacitySample { Timestamp = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc) },
            new ClusterCapacitySample { Timestamp = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc) },
            new ClusterCapacitySample { Timestamp = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc) });
        await context.SaveChangesAsync();

        var result = await NewController(context).GetBillingMonths(default) as OkObjectResult;

        Assert.That(result!.Value, Is.EqualTo(new List<string> { "2026-07", "2026-05" }));
    }

    [Test]
    public async Task GetBillingMonth_RejectsMalformedMonth()
    {
        var (context, connection) = await NewContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var result = await NewController(context).GetBillingMonth("June 2026", default);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }
}
