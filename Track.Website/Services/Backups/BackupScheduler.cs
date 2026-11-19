using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Backups;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Services.Backups;

/// <summary>Central: queues due volumes, advances replace steps, dispatches within the caps, and fails jobs whose agent went quiet.</summary>
public sealed class BackupScheduler(
    ILogger<BackupScheduler> logger,
    TrackContext context,
    BackupCatalogService catalogService,
    BackupJobFactory jobFactory,
    ResticTargets resticTargets,
    ResticRunner restic,
    BackupRuntimeState runtime,
    BackupNotifier notifier,
    IHttpClientFactory httpClientFactory,
    JobLogs jobLogs)
{
    public static readonly BackupJobKind[] AgentKinds =
        [BackupJobKind.Backup, BackupJobKind.Restore, BackupJobKind.Hook, BackupJobKind.Scale, BackupJobKind.Import, BackupJobKind.Verify];

    /// <summary>Jobs that move data through restic, which count against the per-node and total caps.</summary>
    public static bool IsHeavy(BackupJobKind kind) =>
        kind is BackupJobKind.Backup or BackupJobKind.Restore or BackupJobKind.Import or BackupJobKind.Verify;

    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);

    // restic retries an unreachable bucket for many minutes, which would hold up every other job meanwhile.
    private static readonly TimeSpan RepositorySetupTimeout = TimeSpan.FromMinutes(2);

    private readonly ILogger<BackupScheduler> _logger = logger;
    private readonly TrackContext _context = context;
    private readonly BackupCatalogService _catalogService = catalogService;
    private readonly BackupJobFactory _jobFactory = jobFactory;
    private readonly ResticTargets _resticTargets = resticTargets;
    private readonly ResticRunner _restic = restic;
    private readonly BackupRuntimeState _runtime = runtime;
    private readonly BackupNotifier _notifier = notifier;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly JobLogs _jobLogs = jobLogs;

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await _runtime.SchedulerGate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await _catalogService.BuildAsync(cancellationToken);
            var changed = false;

            if (catalog.Configured && !catalog.Settings.Paused)
                changed |= await QueueScheduledAsync(catalog, cancellationToken);
            changed |= await AdvanceReplacesAsync(catalog, cancellationToken);
            changed |= await ExpireAsync(catalog.Settings, cancellationToken);
            if (catalog.Configured)
                changed |= await DispatchAsync(catalog, cancellationToken);

            _runtime.NeedsAttention = catalog.Volumes.Count(v => v.NeedsAttention) + catalog.AnonymousDatabases.Count;
            await HeartbeatAsync(catalog, cancellationToken);

            if (changed)
                await _notifier.ChangedAsync(cancellationToken);
        }
        finally
        {
            _runtime.SchedulerGate.Release();
        }
    }

    public static async Task<List<long>> JobIdsForNodeAsync(TrackContext context, string nodeName, CancellationToken cancellationToken) =>
        await context.BackupJobs.AsNoTracking()
            .Where(j => j.NodeName == nodeName
                && (j.Status == BackupJobStatus.Dispatched || j.Status == BackupJobStatus.Running)
                && AgentKinds.Contains(j.Kind))
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

    private async Task<bool> QueueScheduledAsync(BackupCatalogResult catalog, CancellationToken cancellationToken)
    {
        var settings = catalog.Settings;
        var now = DateTime.UtcNow;
        var lastQueued = await _context.BackupJobs.AsNoTracking()
            .Where(j => j.Kind == BackupJobKind.Backup && j.VolumeKey != null && j.ParentJobId == null)
            .GroupBy(j => j.VolumeKey!)
            .Select(g => new { g.Key, QueuedAt = g.Max(j => j.QueuedAt) })
            .ToDictionaryAsync(g => g.Key, g => g.QueuedAt, cancellationToken);

        var queued = 0;
        foreach (var state in catalog.Volumes.Where(v => v.Volume.Protected && v.ActiveJob is null))
        {
            var slot = BackupSchedule.MostRecentDailySlot(
                now, settings.BackupMinuteUtc, BackupSchedule.OffsetMinutes(state.Volume.Key, settings.SpreadMinutes));
            if (lastQueued.TryGetValue(state.Volume.Key, out var last) && last >= slot)
                continue;

            await _jobFactory.QueueBackupAsync(state, BackupTrigger.Schedule, null, cancellationToken);
            queued++;
        }

        if (queued > 0)
            await _context.SaveChangesAsync(cancellationToken);
        return queued > 0;
    }

    private async Task<bool> AdvanceReplacesAsync(BackupCatalogResult catalog, CancellationToken cancellationToken)
    {
        var parents = await _context.BackupJobs
            .Where(j => j.Kind == BackupJobKind.Replace && j.Status == BackupJobStatus.Running)
            .ToListAsync(cancellationToken);
        if (parents.Count == 0)
            return false;

        var changed = false;
        foreach (var parent in parents)
        {
            var steps = JobParameters.Parse(parent.ParametersJson).Steps ?? [];
            var current = await _context.BackupJobs
                .Where(j => j.ParentJobId == parent.Id && j.Step == parent.Step)
                .OrderByDescending(j => j.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (parent.CancelRequested && (current is null || !current.IsActive))
            {
                Finish(parent, BackupJobStatus.Cancelled, "Cancelled");
                changed = true;
                continue;
            }

            if (current is { IsActive: true })
            {
                if (parent.CancelRequested && !current.CancelRequested)
                {
                    current.CancelRequested = true;
                    if (current.Status == BackupJobStatus.Queued)
                        Finish(current, BackupJobStatus.Cancelled, "Cancelled");
                    changed = true;
                }
                continue;
            }

            if (current is not null && current.Status != BackupJobStatus.Succeeded)
            {
                var stopped = steps.Take(parent.Step).Any(s => s.ScalesDown);
                Finish(parent, BackupJobStatus.Failed,
                    $"{steps[parent.Step].Name} failed: {current.Error ?? "unknown error"}"
                    + (stopped ? " The services are scaled to 0; scale them back once the volume is right." : ""));
                changed = true;
                continue;
            }

            if (current is not null)
                parent.Step++;

            if (parent.Step >= steps.Count)
            {
                Finish(parent, BackupJobStatus.Succeeded, null);
                changed = true;
                continue;
            }

            var step = steps[parent.Step];
            var child = _jobFactory.StepJob(parent, step, parent.Step);
            if (step.Kind == BackupJobKind.Scale)
                child.NodeName = PickManager(catalog, step.NodeName);
            _context.BackupJobs.Add(child);
            changed = true;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private async Task<bool> ExpireAsync(BackupSettings settings, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var quietLimit = TimeSpan.FromMinutes(Math.Max(30, settings.HookTimeoutMinutes + 5));
        var active = await _context.BackupJobs
            .Where(j => (j.Status == BackupJobStatus.Dispatched || j.Status == BackupJobStatus.Running) && AgentKinds.Contains(j.Kind))
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var job in active)
        {
            if (job.Status == BackupJobStatus.Dispatched && now - job.DispatchedAt > DispatchTimeout)
            {
                job.Status = BackupJobStatus.Queued;
                job.DispatchedAt = null;
                changed = true;
            }
            else if (job.Status == BackupJobStatus.Running && now - (job.LastProgressAt ?? job.StartedAt ?? job.QueuedAt) > quietLimit)
            {
                Finish(job, BackupJobStatus.Failed, "The agent stopped reporting");
                await _jobLogs.InSessionAsync(job, () =>
                {
                    _logger.LogWarning("{Node} stopped reporting, so the job was marked failed", job.NodeName);
                    return Task.CompletedTask;
                });
                await _jobLogs.CompleteAsync(job);
                changed = true;
            }
        }

        if (changed)
            await _context.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private async Task<bool> DispatchAsync(BackupCatalogResult catalog, CancellationToken cancellationToken)
    {
        var settings = catalog.Settings;
        var jobs = await _context.BackupJobs
            .Where(j => j.Status != BackupJobStatus.Succeeded && j.Status != BackupJobStatus.Failed && j.Status != BackupJobStatus.Cancelled
                && AgentKinds.Contains(j.Kind))
            .OrderBy(j => j.QueuedAt)
            .ToListAsync(cancellationToken);
        var queued = jobs.Where(j => j.Status == BackupJobStatus.Queued).ToList();
        if (queued.Count == 0)
            return false;

        var busy = jobs.Where(j => j.Status != BackupJobStatus.Queued && IsHeavy(j.Kind)).ToList();
        var perNode = busy.GroupBy(j => j.NodeName ?? "").ToDictionary(g => g.Key, g => g.Count());
        var total = busy.Count;
        var states = catalog.Volumes.ToDictionary(v => v.Volume.Key);
        var repositories = await _context.BackupRepositories.ToDictionaryAsync(r => r.Key, cancellationToken);
        var changed = false;

        foreach (var job in queued)
        {
            if (job.Kind == BackupJobKind.Backup && job.ParentJobId is null)
            {
                if (!states.TryGetValue(job.VolumeKey!, out var state) || !state.Volume.Protected)
                {
                    Finish(job, BackupJobStatus.Cancelled, "The volume is gone or no longer backed up");
                    changed = true;
                    continue;
                }
                job.NodeName = state.Volume.NodeName;
            }
            else if (job.Kind == BackupJobKind.Scale && (job.NodeName is null || !catalog.ReadyNodes.Contains(job.NodeName)))
            {
                job.NodeName = PickManager(catalog, null);
            }

            if (job.NodeName is null || !catalog.ReadyNodes.Contains(job.NodeName))
                continue;

            var heavy = IsHeavy(job.Kind);
            if (heavy && job.Trigger != BackupTrigger.Restore
                && (perNode.GetValueOrDefault(job.NodeName) >= settings.MaxJobsPerNode || total >= settings.MaxJobsTotal))
                continue;

            if (job.RepositoryKey is not null)
            {
                if (_runtime.InMaintenance(job.RepositoryKey))
                    continue;
                if (!repositories.TryGetValue(job.RepositoryKey, out var repository))
                {
                    Finish(job, BackupJobStatus.Failed, $"Repository {job.RepositoryKey} isn't known");
                    await _jobLogs.CompleteAsync(job);
                    changed = true;
                    continue;
                }
                if (repository.InitializedAt is null)
                {
                    var setupError = await SetUpRepositoryAsync(job, repository, catalog.Target!, cancellationToken);
                    if (setupError is not null)
                    {
                        Finish(job, BackupJobStatus.Failed, setupError);
                        await _jobLogs.CompleteAsync(job);
                        changed = true;
                        continue;
                    }
                }
            }

            job.Status = BackupJobStatus.Dispatched;
            job.DispatchedAt = DateTime.UtcNow;
            await _jobLogs.InSessionAsync(job, () =>
            {
                _logger.LogInformation("Waiting for {Node} to pick up the job", job.NodeName);
                return Task.CompletedTask;
            });
            if (heavy)
            {
                perNode[job.NodeName] = perNode.GetValueOrDefault(job.NodeName) + 1;
                total++;
            }
            changed = true;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return changed;
    }

    /// <summary>The reason the job can't go ahead, or null once the repository exists.</summary>
    private async Task<string?> SetUpRepositoryAsync(
        BackupJob job, BackupRepository repository, BackupTarget target, CancellationToken cancellationToken)
    {
        string? error = null;
        await _jobLogs.InSessionAsync(job, async () =>
        {
            _logger.LogInformation("Setting up repository {Key} in {Bucket}", repository.Key, target.Bucket);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RepositorySetupTimeout);
            try
            {
                await InitializeAsync(repository, target, timeout.Token);
                _logger.LogInformation("Repository {Key} is ready", repository.Key);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                error = $"Couldn't reach the backup bucket within {RepositorySetupTimeout.TotalMinutes:0} minutes to set up {repository.Key}";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = $"Couldn't set up repository {repository.Key}: {ex.Message}";
            }

            if (error is not null)
                _logger.LogError("{Error}", error);
        });
        return error;
    }

    private async Task InitializeAsync(BackupRepository repository, BackupTarget target, CancellationToken cancellationToken)
    {
        var restic = _resticTargets.For(target, repository.Key, maintenance: true);
        var probe = await _restic.RunAsync(restic, ["cat", "config"], cancellationToken);
        if (!probe.Succeeded)
        {
            if (probe.ExitCode != ResticRunner.RepositoryMissingExitCode)
                throw new InvalidOperationException(probe.Message);

            var init = await _restic.RunAsync(restic, ["init"], cancellationToken);
            if (!init.Succeeded)
                throw new InvalidOperationException(init.Message);
            _logger.LogInformation("Created backup repository {Key}", repository.Key);
        }

        repository.InitializedAt = DateTime.UtcNow;
    }

    private async Task HeartbeatAsync(BackupCatalogResult catalog, CancellationToken cancellationToken)
    {
        var url = catalog.Settings.HeartbeatUrl;
        if (string.IsNullOrWhiteSpace(url) || !catalog.Configured || catalog.Settings.Paused
            || DateTime.UtcNow - _runtime.LastHeartbeat < HeartbeatInterval)
            return;

        var protectedVolumes = catalog.Volumes.Where(v => v.Volume.Protected).ToList();
        var healthy = protectedVolumes.Count > 0 && protectedVolumes.All(v => v.Status is BackupVolumeStatus.Ok
            or BackupVolumeStatus.Queued or BackupVolumeStatus.Running);
        if (!healthy)
            return;

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync(url, cancellationToken);
            _runtime.LastHeartbeat = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Backup heartbeat ping failed");
        }
    }

    private static string? PickManager(BackupCatalogResult catalog, string? preferred)
    {
        var managers = catalog.Swarm?.Nodes.Where(n => n.Role == "manager").Select(n => n.Hostname).ToHashSet() ?? [];
        if (preferred is not null && catalog.ReadyNodes.Contains(preferred) && managers.Contains(preferred))
            return preferred;
        if (catalog.Swarm is not null && catalog.ReadyNodes.Contains(catalog.Swarm.NodeName))
            return catalog.Swarm.NodeName;
        return catalog.ReadyNodes.FirstOrDefault(managers.Contains);
    }

    public static void Finish(BackupJob job, BackupJobStatus status, string? error)
    {
        job.Status = status;
        job.Error = error;
        job.FinishedAt = DateTime.UtcNow;
    }
}
