using Track.Configuration;
using Track.Website.Services;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

/// <summary>
/// Central-only: polls each enabled S3 source (configured in the DB, managed live from the UI) for per-bucket
/// sizes on a slow interval. Buckets are billed storage, so sources are polled directly by the central - no
/// agent involved. The poll itself lives in <see cref="S3UsageRefresher"/>, which the S3 sources API also calls
/// so a newly added source populates immediately instead of waiting out this timer.
/// </summary>
public sealed class S3UsageHostedService(
    ILogger<S3UsageHostedService> logger,
    IOptions<TrackOptions> options,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    private readonly ILogger<S3UsageHostedService> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(60, _options.Value.S3PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var refresher = scope.ServiceProvider.GetRequiredService<S3UsageRefresher>();
                    await refresher.RefreshAsync(cancellationToken: stoppingToken);
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
}
