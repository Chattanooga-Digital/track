using Track.Configuration;
using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Services.Docker;
using Track.Website.Hubs;
using Track.Website.Queries;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

public sealed class StatsPollingHostedService(
    ILogger<StatsPollingHostedService> logger,
    IOptions<TrackOptions> options,
    DockerStatsCollector statsCollector,
    CurrentStateHolder currentStateHolder,
    IServiceScopeFactory serviceScopeFactory,
    IHubContext<TrackHub> hubContext) : BackgroundService
{
    private readonly ILogger<StatsPollingHostedService> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly DockerStatsCollector _statsCollector = statsCollector;
    private readonly CurrentStateHolder _currentStateHolder = currentStateHolder;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IHubContext<TrackHub> _hubContext = hubContext;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds));
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
                    // The polling loop must survive transient failures (docker restart, db lock); the next tick retries.
                    _logger.LogError(ex, "Stats poll failed");
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
        var timestamp = DateTime.UtcNow;
        var allSamples = new List<ContainerSample>();

        await Task.WhenAll(_options.Value.ResolveNodes().Select(async node =>
        {
            try
            {
                var collection = await _statsCollector.CollectAsync(node, timestamp, cancellationToken);
                _currentStateHolder.SetNodeSamples(node.Name, collection, timestamp);
                lock (allSamples)
                {
                    allSamples.AddRange(collection.Samples);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to poll docker node {Node}", node.Name);
                _currentStateHolder.SetNodeError(node.Name, ex.Message);
            }
        }));

        using var scope = _serviceScopeFactory.CreateScope();
        var metricsContext = scope.ServiceProvider.GetRequiredService<TrackContext>();
        metricsContext.ContainerSamples.AddRange(allSamples);
        await metricsContext.SaveChangesAsync(cancellationToken);

        var snapshot = await scope.ServiceProvider.GetRequiredService<SnapshotBuilder>().BuildAsync(cancellationToken);
        await _hubContext.Clients.All.SendAsync(TrackHub.SnapshotEvent, snapshot, cancellationToken);
    }
}
