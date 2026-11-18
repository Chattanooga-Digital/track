using System.Globalization;
using System.Text.Json;
using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Backups;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Services.Backups;

/// <summary>Central: weekly forget and prune, a monthly partial read check, and each repository's size, with the maintenance key.</summary>
public sealed class BackupMaintenance(
    ILogger<BackupMaintenance> logger,
    TrackContext context,
    ResticTargets resticTargets,
    ResticRunner restic,
    SnapshotIndex snapshotIndex,
    BackupRuntimeState runtime,
    BackupNotifier notifier,
    BackupMasterKey masterKey)
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(28);

    // A cancelled or crashed backup leaves a lock that only goes stale after 30 minutes, so don't retry every minute.
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromHours(1);
    private const string PreRestoreRetention = "30d";

    private readonly ILogger<BackupMaintenance> _logger = logger;
    private readonly TrackContext _context = context;
    private readonly ResticTargets _resticTargets = resticTargets;
    private readonly ResticRunner _restic = restic;
    private readonly SnapshotIndex _snapshotIndex = snapshotIndex;
    private readonly BackupRuntimeState _runtime = runtime;
    private readonly BackupNotifier _notifier = notifier;
    private readonly BackupMasterKey _masterKey = masterKey;

    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        var target = await _context.BackupTargets.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var settings = await _context.BackupSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new BackupSettings();
        var force = _runtime.MaintenanceRequested;
        if (target is null || _masterKey.Value is null || (settings.Paused && !force))
            return;

        var slot = BackupSchedule.MostRecentWeeklySlot(DateTime.UtcNow, settings.PruneDay, settings.PruneMinuteUtc);
        var repositories = await _context.BackupRepositories
            .Where(r => r.InitializedAt != null)
            .ToListAsync(cancellationToken);

        var pending = false;
        foreach (var repository in repositories)
        {
            var due = force || (repository.LastPrunedAt ?? repository.InitializedAt) < slot;
            if (!due || (!force && DateTime.UtcNow - _runtime.MaintenanceFailedAt(repository.Key) < RetryFailedAfter))
                continue;

            var busy = await _context.BackupJobs.AnyAsync(j => j.RepositoryKey == repository.Key
                && (j.Status == BackupJobStatus.Dispatched || j.Status == BackupJobStatus.Running)
                && (j.Kind == BackupJobKind.Backup || j.Kind == BackupJobKind.Restore), cancellationToken);
            if (busy)
            {
                pending = true;
                continue;
            }

            await MaintainAsync(repository, target, settings, cancellationToken);
        }

        if (force && !pending)
            _runtime.MaintenanceRequested = false;
    }

    private async Task MaintainAsync(BackupRepository repository, BackupTarget target, BackupSettings settings, CancellationToken cancellationToken)
    {
        _runtime.EnterMaintenance(repository.Key);
        try
        {
            var resticTarget = _resticTargets.For(target, repository.Key, maintenance: true);

            var pruned = await RunJobAsync(BackupJobKind.Prune, repository, async output =>
            {
                await Step(output, resticTarget, ["unlock"], cancellationToken);
                await Step(output, resticTarget,
                [
                    "forget", "--group-by", "host", "--tag", BackupSnapshot.Tag,
                    "--keep-daily", Number(settings.KeepDaily),
                    "--keep-weekly", Number(settings.KeepWeekly),
                    "--keep-monthly", Number(settings.KeepMonthly),
                ], cancellationToken);
                await Step(output, resticTarget,
                    ["forget", "--group-by", "host", "--tag", BackupSnapshot.PreRestoreTag, "--keep-within", PreRestoreRetention],
                    cancellationToken);
                await Step(output, resticTarget, ["prune"], cancellationToken);

                var stats = await Step(output, resticTarget, ["stats", "--mode", "raw-data", "--json"], cancellationToken);
                using var document = JsonDocument.Parse(stats);
                repository.SizeBytes = document.RootElement.GetProperty("total_size").GetInt64();
                repository.SizeUpdatedAt = DateTime.UtcNow;

                await _snapshotIndex.SyncAsync(repository.Key, resticTarget, cancellationToken);
                repository.LastPrunedAt = DateTime.UtcNow;
            }, cancellationToken);

            if (pruned && (repository.LastCheckedAt is null || DateTime.UtcNow - repository.LastCheckedAt > CheckEvery || _runtime.MaintenanceRequested))
            {
                await RunJobAsync(BackupJobKind.Check, repository, async output =>
                {
                    await Step(output, resticTarget, ["check", $"--read-data-subset={Math.Clamp(settings.CheckPercent, 1, 100)}%"], cancellationToken);
                    repository.LastCheckedAt = DateTime.UtcNow;
                }, cancellationToken);
            }
        }
        finally
        {
            _runtime.ExitMaintenance(repository.Key);
        }
    }

    private async Task<bool> RunJobAsync(BackupJobKind kind, BackupRepository repository, Func<OutputLog, Task> work, CancellationToken cancellationToken)
    {
        var job = new BackupJob
        {
            Kind = kind,
            Status = BackupJobStatus.Running,
            Trigger = BackupTrigger.Schedule,
            RepositoryKey = repository.Key,
            QueuedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
        };
        _context.BackupJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);
        await _notifier.ChangedAsync(cancellationToken);

        var output = new OutputLog();
        try
        {
            await work(output);
            BackupScheduler.Finish(job, BackupJobStatus.Succeeded, null);
            repository.LastMaintenanceError = null;
            _runtime.RecordMaintenanceResult(repository.Key, succeeded: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Kind} of backup repository {Key} failed", kind, repository.Key);
            BackupScheduler.Finish(job, BackupJobStatus.Failed, ex.Message);
            repository.LastMaintenanceError = $"{kind}: {ex.Message}";
            _runtime.RecordMaintenanceResult(repository.Key, succeeded: false);
        }

        job.OutputTail = output.ToString();
        await _context.SaveChangesAsync(CancellationToken.None);
        await _notifier.ChangedAsync(CancellationToken.None);
        return job.Status == BackupJobStatus.Succeeded;
    }

    private async Task<string> Step(OutputLog output, ResticTarget target, List<string> arguments, CancellationToken cancellationToken)
    {
        output.Append($"$ restic {string.Join(' ', arguments)}");
        var result = await _restic.RunAsync(target, arguments, cancellationToken);
        foreach (var line in (result.StandardOutput + "\n" + result.StandardError).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            output.Append(line.Length > 500 ? line[..500] : line);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.Message);
        return result.StandardOutput;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
