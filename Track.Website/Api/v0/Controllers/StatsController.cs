using Track.Data;
using Track.Enums;
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
