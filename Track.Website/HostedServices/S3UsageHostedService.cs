using Track.Configuration;
using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Services.S3;
using Track.Website.Hubs;
using Track.Website.Queries;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

/// <summary>
/// Central-only: polls each configured S3 source for per-bucket sizes on a slow interval, persists the
/// readings as bucket disk samples, merges them into the live state, and rebroadcasts the snapshot.
/// Buckets are billed storage, so sources are polled directly by the central - no agent involved.
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
        var sources = ValidSources();
        if (sources.Count == 0)
            return;

        var interval = TimeSpan.FromSeconds(Math.Max(60, _options.Value.S3PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                try
                {
                    await PollAsync(sources, stoppingToken);
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

    private List<S3SourceOptions> ValidSources()
    {
        var sources = new List<S3SourceOptions>();
        foreach (var source in _options.Value.S3Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Name) || string.IsNullOrWhiteSpace(source.Endpoint))
                _logger.LogWarning("Ignoring S3 source with missing Name or Endpoint");
            else
                sources.Add(source);
        }

        return sources;
    }

    private async Task PollAsync(List<S3SourceOptions> sources, CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var collected = new List<DiskSample>();

        foreach (var source in sources)
        {
            // Per-source isolation: one unreachable endpoint must not blank the others. The previous
            // reading for a failed source stays current - storage doesn't vanish when its API is down.
            try
            {
                var samples = await _collector.CollectAsync(source, timestamp, cancellationToken);
                _currentStateHolder.SetBucketUsage(source.Name!, samples);
                collected.AddRange(samples);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "S3 usage poll failed for source {Source}", source.Name);
            }
        }

        if (collected.Count == 0)
            return;

        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TrackContext>();
        context.DiskSamples.AddRange(collected);
        await context.SaveChangesAsync(cancellationToken);

        var snapshot = await scope.ServiceProvider.GetRequiredService<SnapshotBuilder>().BuildAsync(cancellationToken);
        await _hubContext.Clients.All.SendAsync(TrackHub.SnapshotEvent, snapshot, cancellationToken);
    }
}
