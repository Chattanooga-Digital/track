using Track.Configuration;
using Track.Website.Services;
using Microsoft.Extensions.Options;

namespace Track.Website.HostedServices;

/// <summary>
/// Central-only: records a billing tick every <see cref="TrackOptions.BillingSampleIntervalSeconds"/>.
/// The recording itself lives in <see cref="BillingSampleRecorder"/>.
/// </summary>
public sealed class BillingSampleHostedService(
    ILogger<BillingSampleHostedService> logger,
    IOptions<TrackOptions> options,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    private readonly ILogger<BillingSampleHostedService> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(60, _options.Value.BillingSampleIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            // Wait one interval before the first tick so agents have pushed and capacity is known.
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var recorder = scope.ServiceProvider.GetRequiredService<BillingSampleRecorder>();
                    await recorder.RecordAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Billing sample tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }
}
