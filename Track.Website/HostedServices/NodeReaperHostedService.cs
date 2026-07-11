using Track.Configuration;
using Track.Services;
using Track.Website.Hubs;
using Track.Website.Queries;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

/// <summary>
/// Central-only: marks an agent offline once it stops pushing (a node crash, network partition, or a
/// container that moved), dropping its stale samples so they no longer count toward totals, and rebroadcasts
/// the corrected snapshot. Replaces the per-poll reachability check the direct-poll model had.
/// </summary>
public sealed class NodeReaperHostedService(
    ILogger<NodeReaperHostedService> logger,
    IOptions<TrackOptions> options,
    CurrentStateHolder currentStateHolder,
    ContainerInventoryHolder inventoryHolder,
    IServiceScopeFactory serviceScopeFactory,
    IHubContext<TrackHub> hubContext) : BackgroundService
{
    private readonly ILogger<NodeReaperHostedService> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly CurrentStateHolder _currentStateHolder = currentStateHolder;
    private readonly ContainerInventoryHolder _inventoryHolder = inventoryHolder;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IHubContext<TrackHub> _hubContext = hubContext;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check on the stats cadence so an offline node surfaces within roughly one push interval of the timeout.
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.Value.PollIntervalSeconds, 5, 60));
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                try
                {
                    await ReapAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Node reaping failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task ReapAsync(CancellationToken cancellationToken)
    {
        var maxAge = TimeSpan.FromSeconds(Math.Max(5, _options.Value.NodeOfflineAfterSeconds));
        var expired = _currentStateHolder.ExpireStaleNodes(maxAge, DateTime.UtcNow);
        if (expired.Count == 0)
            return;

        // Drop the offline nodes' label-less inventory too, so the UI stops offering their containers.
        _inventoryHolder.RemoveNodes(expired);

        using var scope = _serviceScopeFactory.CreateScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<SnapshotBuilder>().BuildAsync(cancellationToken);
        await _hubContext.Clients.All.SendAsync(TrackHub.SnapshotEvent, snapshot, cancellationToken);
    }
}
