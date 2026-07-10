using Track.Configuration;
using Track.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

public sealed class SamplePruningHostedService(
    ILogger<SamplePruningHostedService> logger,
    IOptions<TrackOptions> options,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    private readonly ILogger<SamplePruningHostedService> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        try
        {
            do
            {
                try
                {
                    await PruneAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Sample pruning failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.Value.RetentionDays));

        using var scope = _serviceScopeFactory.CreateScope();
        var metricsContext = scope.ServiceProvider.GetRequiredService<TrackContext>();

        var containerSamples = await metricsContext.ContainerSamples
            .Where(s => s.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var diskSamples = await metricsContext.DiskSamples
            .Where(s => s.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (containerSamples > 0 || diskSamples > 0)
            _logger.LogInformation(
                "Pruned {ContainerSamples} container samples and {DiskSamples} disk samples older than {Cutoff}",
                containerSamples, diskSamples, cutoff);
    }
}
