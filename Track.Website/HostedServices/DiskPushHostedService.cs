using Track.Configuration;
using Track.Services.Docker;
using Track.Services.Ingest;
using Track.Website.Services;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

/// <summary>
/// Reads the local node's disk usage on the slow interval and pushes it to the central instance.
/// /system/df is heavy, so this runs far less often than the stats push.
/// </summary>
public sealed class DiskPushHostedService(
    ILogger<DiskPushHostedService> logger,
    IOptions<TrackOptions> options,
    DockerDiskCollector diskCollector,
    AgentNodeNameResolver nodeNameResolver,
    CentralClient centralClient) : BackgroundService
{
    private readonly ILogger<DiskPushHostedService> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly DockerDiskCollector _diskCollector = diskCollector;
    private readonly AgentNodeNameResolver _nodeNameResolver = nodeNameResolver;
    private readonly CentralClient _centralClient = centralClient;

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
                    await PushAsync(stoppingToken);
                }
                // Filter on the token, not the type: a timeout's TaskCanceledException must retry, not kill the loop.
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Disk push failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private async Task PushAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var nodeName = await _nodeNameResolver.ResolveAsync(cancellationToken);
        var node = new DockerNodeOptions { Name = nodeName, Endpoint = TrackOptions.LocalDockerSocket };

        var samples = await _diskCollector.CollectAsync(node, timestamp, cancellationToken);
        await _centralClient.PostDiskAsync(new DiskReport(node.Name, timestamp, samples), cancellationToken);
    }
}
