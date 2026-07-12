using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services;
using Track.Services.S3;
using Track.Website.Hubs;
using Track.Website.Queries;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Services;

/// <summary>
/// Polls the enabled S3 sources once: persists the readings, merges them into the live state, and
/// rebroadcasts. Shared by <see cref="HostedServices.S3UsageHostedService"/> (on its slow timer) and by
/// the S3 sources API, so adding or editing a source shows its buckets immediately instead of after a
/// poll interval.
/// </summary>
public sealed class S3UsageRefresher(
    ILogger<S3UsageRefresher> logger,
    TrackContext trackContext,
    S3SourceSecretProtector secretProtector,
    S3UsageCollector collector,
    CurrentStateHolder currentStateHolder,
    SnapshotBuilder snapshotBuilder,
    IHubContext<TrackHub> hubContext)
{
    private readonly ILogger<S3UsageRefresher> _logger = logger;
    private readonly TrackContext _trackContext = trackContext;
    private readonly S3SourceSecretProtector _secretProtector = secretProtector;
    private readonly S3UsageCollector _collector = collector;
    private readonly CurrentStateHolder _currentStateHolder = currentStateHolder;
    private readonly SnapshotBuilder _snapshotBuilder = snapshotBuilder;
    private readonly IHubContext<TrackHub> _hubContext = hubContext;

    /// <summary>
    /// Polls every enabled source, or just <paramref name="sourceName"/> when given. Returns the number of
    /// buckets read. Only a full sweep reconciles the live state, since a single-source poll knows nothing
    /// about the sources it skipped.
    /// </summary>
    public async Task<int> RefreshAsync(string? sourceName = null, CancellationToken cancellationToken = default)
    {
        var enabled = await _trackContext.S3Sources
            .AsNoTracking()
            .Where(s => s.Enabled)
            .ToListAsync(cancellationToken);

        if (sourceName is null)
        {
            // Drop cached buckets for sources that were deleted or disabled since the last poll.
            _currentStateHolder.RetainBucketSources(enabled.Select(s => s.Name).ToHashSet());
        }

        var sources = sourceName is null
            ? enabled
            : enabled.Where(s => string.Equals(s.Name, sourceName, StringComparison.OrdinalIgnoreCase)).ToList();

        var timestamp = DateTime.UtcNow;
        var collected = new List<DiskSample>();

        foreach (var source in sources)
        {
            // Per-source isolation: one unreachable endpoint must not blank the others. The previous
            // reading for a failed source stays current - storage doesn't vanish when its API is down.
            try
            {
                var target = ToTarget(source);
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
            _trackContext.DiskSamples.AddRange(collected);
            await _trackContext.SaveChangesAsync(cancellationToken);
        }

        await BroadcastAsync(cancellationToken);

        return collected.Count;
    }

    /// <summary>Pushes the current snapshot to every connected client.</summary>
    public async Task BroadcastAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotBuilder.BuildAsync(cancellationToken);
        await _hubContext.Clients.All.SendAsync(TrackHub.SnapshotEvent, snapshot, cancellationToken);
    }

    private S3PollTarget ToTarget(S3Source source) => new(
        source.Name,
        source.Endpoint,
        source.Kind,
        source.AccessKey,
        _secretProtector.Unprotect(source.SecretKeyProtected),
        source.Region);
}
