using Track.Services.Docker;
using Track.Website.Services;

namespace Track.Website.HostedServices;

/// <summary>Agent: on a manager node, pushes the services, tasks and nodes that only a manager's engine can list.</summary>
public sealed class SwarmInventoryPushHostedService(
    ILogger<SwarmInventoryPushHostedService> logger,
    SwarmInventoryCollector collector,
    AgentNodeNameResolver nodeNameResolver,
    CentralClient centralClient) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly ILogger<SwarmInventoryPushHostedService> _logger = logger;
    private readonly SwarmInventoryCollector _collector = collector;
    private readonly AgentNodeNameResolver _nodeNameResolver = nodeNameResolver;
    private readonly CentralClient _centralClient = centralClient;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    var nodeName = await _nodeNameResolver.ResolveAsync(stoppingToken);
                    if (await _collector.CollectAsync(nodeName, stoppingToken) is { } report)
                        await _centralClient.PostSwarmAsync(report, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Swarm inventory push failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
