using Track.Data;
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
        var diskQuery = TrackContext.DiskSamples.AsNoTracking().Where(s => s.Timestamp >= from);
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

        return Ok(HistoryAggregator.Aggregate(usagePoints, diskPoints, from, to));
    }
}
