using Track.Website.Services.Backups;

namespace Track.Website.HostedServices;

/// <summary>Central: runs a <see cref="BackupScheduler"/> pass every 30 seconds, or sooner when something asks for one.</summary>
public sealed class BackupSchedulerHostedService(
    ILogger<BackupSchedulerHostedService> logger,
    IServiceScopeFactory scopeFactory,
    BackupRuntimeState runtime) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly ILogger<BackupSchedulerHostedService> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly BackupRuntimeState _runtime = runtime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await _runtime.WaitForWakeAsync(Interval, stoppingToken);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<BackupScheduler>().TickAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Backup scheduler pass failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
