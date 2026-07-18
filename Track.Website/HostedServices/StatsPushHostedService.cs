using Track.Configuration;
using Track.Services.Docker;
using Track.Services.Ingest;
using Track.Website.Services;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

/// <summary>
/// Collects the local node's container stats each interval and pushes them to the central instance
/// instead of writing to a local DB. The central's ingest endpoint stores and broadcasts them.
/// </summary>
public sealed class StatsPushHostedService(
    ILogger<StatsPushHostedService> logger,
    IOptions<TrackOptions> options,
    DockerStatsCollector statsCollector,
    AgentNodeNameResolver nodeNameResolver,
    IdentityEnvVarProvider identityEnvVars,
    CentralClient centralClient) : BackgroundService
{
    private readonly ILogger<StatsPushHostedService> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly DockerStatsCollector _statsCollector = statsCollector;
    private readonly AgentNodeNameResolver _nodeNameResolver = nodeNameResolver;
    private readonly IdentityEnvVarProvider _identityEnvVars = identityEnvVars;
    private readonly CentralClient _centralClient = centralClient;

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
                    await PushAsync(stoppingToken);
                }
                // Filter on the token, not the type: a timeout's TaskCanceledException must retry, not kill the loop.
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Stats push failed");
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

        var collection = await _statsCollector.CollectAsync(node, timestamp, cancellationToken);
        var report = new StatsReport(
            node.Name, timestamp, collection.Capacity, collection.Samples, collection.Inventory);

        var response = await _centralClient.PostStatsAsync(report, cancellationToken);

        // Apply the central's authoritative identity env-var list so grouping is driven from the UI.
        if (response is not null)
            _identityEnvVars.Update(response.IdentityEnvVars);
    }
}
