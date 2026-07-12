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

        // Billing ticks are the invoicing record, so they live on their own (much longer) retention.
        var billingCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.Value.BillingRetentionDays));
        var billingSamples = await metricsContext.BillingSamples
            .Where(s => s.Timestamp < billingCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var capacitySamples = await metricsContext.ClusterCapacitySamples
            .Where(s => s.Timestamp < billingCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (containerSamples > 0 || diskSamples > 0 || billingSamples > 0 || capacitySamples > 0)
            _logger.LogInformation(
                "Pruned {ContainerSamples} container samples, {DiskSamples} disk samples, and {BillingSamples} billing samples older than retention",
                containerSamples, diskSamples, billingSamples + capacitySamples);
    }
}
