using Track.Data;
using Track.Enums;
using Track.Website.Services.Backups;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.HostedServices;

/// <summary>Central: checks every minute for repositories due their weekly prune.</summary>
public sealed class BackupMaintenanceHostedService(
    ILogger<BackupMaintenanceHostedService> logger,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ILogger<BackupMaintenanceHostedService> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await FailInterruptedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<BackupMaintenance>().RunDueAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Backup maintenance pass failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    // Prune and check run inside this process, so any still marked running died with the previous one.
    private async Task FailInterruptedAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TrackContext>();
        await context.BackupJobs
            .Where(j => (j.Kind == BackupJobKind.Prune || j.Kind == BackupJobKind.Check) && j.Status == BackupJobStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BackupJobStatus.Failed)
                .SetProperty(j => j.Error, "Track restarted while this was running")
                .SetProperty(j => j.FinishedAt, DateTime.UtcNow), cancellationToken);
    }
}
