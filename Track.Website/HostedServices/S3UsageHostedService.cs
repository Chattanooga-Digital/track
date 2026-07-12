using Track.Configuration;
using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services;
using Track.Services.S3;
using Track.Website.Hubs;
using Track.Website.Queries;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

/// <summary>
/// Central-only: polls each enabled S3 source (configured in the DB, managed live from the UI) for per-bucket
/// sizes on a slow interval, persists the readings, merges them into the live state, and rebroadcasts. Buckets
/// are billed storage, so sources are polled directly by the central - no agent involved.
/// </summary>
public sealed class S3UsageHostedService(
    ILogger<S3UsageHostedService> logger,
    IOptions<TrackOptions> options,
    S3UsageCollector collector,
    CurrentStateHolder currentStateHolder,
    IServiceScopeFactory serviceScopeFactory,
    IHubContext<TrackHub> hubContext) : BackgroundService
{
    private readonly ILogger<S3UsageHostedService> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly S3UsageCollector _collector = collector;
    private readonly CurrentStateHolder _currentStateHolder = currentStateHolder;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IHubContext<TrackHub> _hubContext = hubContext;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(60, _options.Value.S3PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                try
                {
                    await PollAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "S3 usage poll failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TrackContext>();
        var protector = scope.ServiceProvider.GetRequiredService<S3SourceSecretProtector>();

        var sources = await context.S3Sources
            .AsNoTracking()
            .Where(s => s.Enabled)
            .ToListAsync(cancellationToken);

        // Drop cached buckets for sources that were deleted or disabled since the last poll.
        _currentStateHolder.RetainBucketSources(sources.Select(s => s.Name).ToHashSet());

        var timestamp = DateTime.UtcNow;
        var collected = new List<DiskSample>();

        foreach (var source in sources)
        {
            // Per-source isolation: one unreachable endpoint must not blank the others. The previous
            // reading for a failed source stays current - storage doesn't vanish when its API is down.
            try
            {
                var target = ToTarget(source, protector);
                var readings = await _collector.CollectAsync(target, cancellationToken);
                _currentStateHolder.SetBucketUsage(source.Name, readings);
                collected.AddRange(readings.Select(r => new DiskSample
                {
                    Timestamp = timestamp,
                    NodeName = r.SourceName,
                    Kind = DiskUsageKind.Bucket,
                    Name = r.BucketName,
                    SizeBytes = r.SizeBytes,
                }));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "S3 usage poll failed for source {Source}", source.Name);
            }
        }

        if (collected.Count > 0)
        {
            context.DiskSamples.AddRange(collected);
            await context.SaveChangesAsync(cancellationToken);
        }

        var snapshot = await scope.ServiceProvider.GetRequiredService<SnapshotBuilder>().BuildAsync(cancellationToken);
        await _hubContext.Clients.All.SendAsync(TrackHub.SnapshotEvent, snapshot, cancellationToken);
    }

    private S3PollTarget ToTarget(S3Source source, S3SourceSecretProtector protector) => new(
        source.Name,
        source.Endpoint,
        source.Kind,
        source.AccessKey,
        protector.Unprotect(source.SecretKeyProtected),
        source.Region);
}
