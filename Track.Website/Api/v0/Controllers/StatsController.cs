using System.Globalization;
using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Billing;
using Track.Website.Api.v0.Models.Stats;
using Track.Website.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

[ApiController]
[Authorize]
[Route("/api/v0/stats")]
public class StatsController(
    ILogger<StatsController> logger,
    TrackContext metricsContext,
    SnapshotBuilder snapshotBuilder
) : ControllerBase
{
    protected ILogger<StatsController> Logger { get; } = logger;
    protected TrackContext TrackContext { get; } = metricsContext;
    protected SnapshotBuilder SnapshotBuilder { get; } = snapshotBuilder;

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        return Ok(await SnapshotBuilder.BuildAsync(cancellationToken));
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? stackName,
        [FromQuery] int? customerId,
        [FromQuery] int hours = 24,
        CancellationToken cancellationToken = default)
    {
        hours = Math.Clamp(hours, 1, 24 * 90);
        var to = DateTime.UtcNow;
        var from = to.AddHours(-hours);

        List<string>? stackNames = null;
        if (customerId is not null)
            stackNames = await TrackContext.StackAssignments
                .Where(a => a.CustomerId == customerId)
                .Select(a => a.StackName)
                .ToListAsync(cancellationToken);
        else if (!string.IsNullOrEmpty(stackName))
            stackNames = [stackName];

        var usageQuery = TrackContext.ContainerSamples.AsNoTracking().Where(s => s.Timestamp >= from);
        // Node-local disk (container layers + volumes) only; bucket storage is its own series below.
        var diskQuery = TrackContext.DiskSamples.AsNoTracking()
            .Where(s => s.Timestamp >= from && s.Kind != DiskUsageKind.Bucket);
        if (stackNames is not null)
        {
            usageQuery = usageQuery.Where(s => s.StackName != null && stackNames.Contains(s.StackName));
            diskQuery = diskQuery.Where(s => s.StackName != null && stackNames.Contains(s.StackName));
        }

        var usagePoints = await usageQuery
            .Select(s => new HistoryAggregator.UsagePoint(s.Timestamp, s.CpuPercent, s.MemoryUsedBytes, s.MemoryLimitBytes))
            .ToListAsync(cancellationToken);
        var diskPoints = await diskQuery
            .Select(s => new HistoryAggregator.DiskPoint(s.Timestamp, s.SizeBytes))
            .ToListAsync(cancellationToken);
        var objectStoragePoints = await ObjectStoragePointsAsync(customerId, stackName, from, cancellationToken);

        return Ok(HistoryAggregator.Aggregate(usagePoints, diskPoints, objectStoragePoints, from, to));
    }

    /// <summary>Months (yyyy-MM, newest first) that have at least one billing tick recorded.</summary>
    [HttpGet("billing/months")]
    public async Task<IActionResult> GetBillingMonths(CancellationToken cancellationToken)
    {
        var months = await TrackContext.ClusterCapacitySamples.AsNoTracking()
            .Select(s => new { s.Timestamp.Year, s.Timestamp.Month })
            .Distinct()
            .OrderByDescending(m => m.Year)
            .ThenByDescending(m => m.Month)
            .ToListAsync(cancellationToken);

        return Ok(months.Select(m => $"{m.Year:D4}-{m.Month:D2}").ToList());
    }

    /// <summary>A month's billing figures, averaged over its recorded billing ticks.</summary>
    [HttpGet("billing")]
    public async Task<IActionResult> GetBillingMonth([FromQuery] string month, CancellationToken cancellationToken)
    {
        if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return BadRequest(new { error = "month must be in yyyy-MM form" });

        var from = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        var to = from.AddMonths(1);

        var capacityQuery = TrackContext.ClusterCapacitySamples.AsNoTracking()
            .Where(s => s.Timestamp >= from && s.Timestamp < to);
        var sampleCount = await capacityQuery.CountAsync(cancellationToken);

        var model = new BillingMonthModel { Month = month, From = from, To = to, SampleCount = sampleCount };
        if (sampleCount == 0)
            return Ok(model);

        model.Capacity = new BillingCapacityModel
        {
            NodeCount = await capacityQuery.AverageAsync(s => (double)s.NodeCount, cancellationToken),
            CpuCores = await capacityQuery.AverageAsync(s => (double)s.CpuCores, cancellationToken),
            MemoryBytes = await capacityQuery.AverageAsync(s => (double)s.MemoryBytes, cancellationToken),
        };

        var groups = await TrackContext.BillingSamples.AsNoTracking()
            .Where(s => s.Timestamp >= from && s.Timestamp < to)
            .GroupBy(s => s.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                CustomerName = g.Max(s => s.CustomerName),
                CpuPercent = g.Sum(s => s.CpuPercent),
                MemoryUsedBytes = g.Sum(s => (double)s.MemoryUsedBytes),
                DiskBytes = g.Sum(s => (double)s.DiskBytes),
                ObjectStorageBytes = g.Sum(s => (double)s.ObjectStorageBytes),
                CpuPercentOfCluster = g.Sum(s => s.CpuPercentOfCluster),
                MemoryPercentOfCluster = g.Sum(s => s.MemoryPercentOfCluster),
                BlendedPercentOfCluster = g.Sum(s => s.BlendedPercentOfCluster),
            })
            .ToListAsync(cancellationToken);

        // Prefer the customer's current name over the recorded one; the stored name only serves deleted customers.
        var currentNames = await TrackContext.Customers.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        // Sums divide by the month's total tick count, not the customer's own row count, so
        // presence for part of the month scales the bill down proportionally.
        model.Customers = groups
            .Select(g => new BillingCustomerModel
            {
                CustomerId = g.CustomerId,
                CustomerName = g.CustomerId is int id && currentNames.TryGetValue(id, out var name) ? name : g.CustomerName,
                CpuPercent = g.CpuPercent / sampleCount,
                MemoryUsedBytes = g.MemoryUsedBytes / sampleCount,
                DiskBytes = g.DiskBytes / sampleCount,
                ObjectStorageBytes = g.ObjectStorageBytes / sampleCount,
                CpuPercentOfCluster = g.CpuPercentOfCluster / sampleCount,
                MemoryPercentOfCluster = g.MemoryPercentOfCluster / sampleCount,
                BlendedPercentOfCluster = g.BlendedPercentOfCluster / sampleCount,
            })
            .OrderByDescending(c => c.BlendedPercentOfCluster)
            .ToList();

        // Dollars use the current rate against the month's averages (rates re-price history rather than freeze).
        var rate = await TrackContext.BillingRates.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new BillingRate();
        model.Currency = rate.Currency;
        var totalCustomerBlended = model.Customers
            .Where(c => c.CustomerId is not null)
            .Sum(c => c.BlendedPercentOfCluster);
        var baseRate = BillingCostCalculator.ComputeBaseRate(rate.ClusterComputeMonthlyCost, totalCustomerBlended);
        foreach (var c in model.Customers)
        {
            var assigned = c.CustomerId is not null;
            var costs = BillingCostCalculator.ForScope(
                assigned ? c.BlendedPercentOfCluster : 0,
                baseRate,
                assigned ? c.DiskBytes : 0,
                assigned ? c.ObjectStorageBytes : 0,
                rate);
            c.ComputeCost = costs.ComputeCost;
            c.DiskCost = costs.DiskCost;
            c.ObjectStorageCost = costs.ObjectStorageCost;
            c.TotalCost = costs.TotalCost;
        }
        model.ComputeCost = model.Customers.Sum(c => c.ComputeCost);
        model.DiskCost = model.Customers.Sum(c => c.DiskCost);
        model.ObjectStorageCost = model.Customers.Sum(c => c.ObjectStorageCost);
        model.TotalCost = model.ComputeCost + model.DiskCost + model.ObjectStorageCost;

        return Ok(model);
    }

    /// <summary>
    /// Bucket-storage samples for the history scope: all buckets cluster-wide, a customer's assigned buckets,
    /// or none when scoped to a single container stack (a stack has no buckets).
    /// </summary>
    private async Task<List<HistoryAggregator.DiskPoint>> ObjectStoragePointsAsync(
        int? customerId, string? stackName, DateTime from, CancellationToken cancellationToken)
    {
        if (customerId is null && !string.IsNullOrEmpty(stackName))
            return [];

        var query = TrackContext.DiskSamples.AsNoTracking()
            .Where(s => s.Timestamp >= from && s.Kind == DiskUsageKind.Bucket);

        if (customerId is not null)
        {
            var assignments = await TrackContext.BucketAssignments
                .Where(a => a.CustomerId == customerId)
                .Select(a => new { a.SourceName, a.BucketName })
                .ToListAsync(cancellationToken);
            if (assignments.Count == 0)
                return [];

            // Narrow to the assigned sources in SQL (Kind.Bucket stores source in NodeName, bucket in Name),
            // then match exact (source, bucket) pairs in memory since EF can't translate a tuple Contains.
            var sources = assignments.Select(a => a.SourceName).Distinct().ToList();
            var rows = await query
                .Where(s => sources.Contains(s.NodeName))
                .Select(s => new { s.Timestamp, s.NodeName, s.Name, s.SizeBytes })
                .ToListAsync(cancellationToken);

            var pairs = assignments
                .Select(a => (a.SourceName, a.BucketName))
                .ToHashSet();
            return rows
                .Where(r => pairs.Contains((r.NodeName, r.Name)))
                .Select(r => new HistoryAggregator.DiskPoint(r.Timestamp, r.SizeBytes))
                .ToList();
        }

        return await query
            .Select(s => new HistoryAggregator.DiskPoint(s.Timestamp, s.SizeBytes))
            .ToListAsync(cancellationToken);
    }
}
