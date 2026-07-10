using Track.Configuration;
using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Services.Docker;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

public sealed class DiskPollingHostedService(
    ILogger<DiskPollingHostedService> logger,
    IOptions<TrackOptions> options,
    DockerDiskCollector diskCollector,
    CurrentStateHolder currentStateHolder,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    private readonly ILogger<DiskPollingHostedService> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly DockerDiskCollector _diskCollector = diskCollector;
    private readonly CurrentStateHolder _currentStateHolder = currentStateHolder;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, _options.Value.DiskPollIntervalSeconds));
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
                    // /system/df can be slow or fail under load; the next tick retries.
                    _logger.LogError(ex, "Disk poll failed");
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
        var allSamples = new List<DiskSample>();

        await Task.WhenAll(_options.Value.ResolveNodes().Select(async node =>
        {
            try
            {
                var samples = await _diskCollector.CollectAsync(node, timestamp, cancellationToken);
                _currentStateHolder.SetNodeDisk(node.Name, samples);
                lock (allSamples)
                {
                    allSamples.AddRange(samples);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to poll disk usage on docker node {Node}", node.Name);
            }
        }));

        using var scope = _serviceScopeFactory.CreateScope();
        var metricsContext = scope.ServiceProvider.GetRequiredService<TrackContext>();
        metricsContext.DiskSamples.AddRange(allSamples);
        await metricsContext.SaveChangesAsync(cancellationToken);
    }
}
