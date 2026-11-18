using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Backups;
using Track.Services.Ingest;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Services.Backups;

/// <summary>Central: creates backup, restore and replace jobs. Callers save the context.</summary>
public sealed class BackupJobFactory(TrackContext context)
{
    private readonly TrackContext _context = context;

    public async Task EnsureRepositoryAsync(BackupVolumeState state, CancellationToken cancellationToken)
    {
        var repository = await _context.BackupRepositories.SingleOrDefaultAsync(r => r.Key == state.RepositoryKey, cancellationToken)
            ?? _context.BackupRepositories.Local.SingleOrDefault(r => r.Key == state.RepositoryKey);
        if (repository is null)
        {
            _context.BackupRepositories.Add(new BackupRepository
            {
                Key = state.RepositoryKey,
                CustomerId = state.CustomerId,
                CustomerName = state.CustomerName,
                IsShared = state.IsShared,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else if (state.CustomerName is not null && repository.CustomerName != state.CustomerName)
        {
            repository.CustomerName = state.CustomerName;
        }
    }

    public async Task<BackupJob> QueueBackupAsync(
        BackupVolumeState state, BackupTrigger trigger, string? requestedBy, CancellationToken cancellationToken)
    {
        await EnsureRepositoryAsync(state, cancellationToken);
        var job = NewJob(BackupJobKind.Backup, trigger, state, state.Volume.NodeName, state.RepositoryKey, BackupParameters(state, preRestore: false));
        job.RequestedBy = requestedBy;
        _context.BackupJobs.Add(job);
        return job;
    }

    public BackupJob QueueRestoreToNewVolume(BackupSnapshot snapshot, string nodeName, string? requestedBy)
    {
        var job = new BackupJob
        {
            Kind = BackupJobKind.Restore,
            Status = BackupJobStatus.Queued,
            Trigger = BackupTrigger.Restore,
            VolumeKey = snapshot.VolumeKey,
            VolumeName = snapshot.VolumeName,
            NodeName = nodeName,
            RepositoryKey = snapshot.RepositoryKey,
            StackName = snapshot.StackName,
            QueuedAt = DateTime.UtcNow,
            RequestedBy = requestedBy,
            ParametersJson = new JobParameters
            {
                SnapshotId = snapshot.SnapshotId,
                SnapshotPath = SnapshotPath(snapshot),
                RestoreVolume = BackupCatalog.RestoreVolumeName(snapshot.VolumeName, DateTime.UtcNow),
            }.Serialize(),
        };
        _context.BackupJobs.Add(job);
        return job;
    }

    public async Task<BackupJob> QueueReplaceAsync(
        BackupVolumeState state, BackupSnapshot snapshot, SwarmReport? swarm, string? managerNode, string? requestedBy,
        CancellationToken cancellationToken)
    {
        await EnsureRepositoryAsync(state, cancellationToken);
        var volume = state.Volume;
        var node = volume.NodeName!;
        var services = swarm?.Services.Where(s => s.Volumes.Contains(volume.VolumeName)).ToList() ?? [];
        var hooksCanRun = volume.Running || services.Count > 0;

        // No hooks or path filters: the app is often broken when restoring, and this must hold exactly what gets overwritten.
        var safety = BackupParameters(state, preRestore: true) with { PreHooks = [], PostHooks = [], Paths = [] };
        var steps = new List<ReplaceStep> { new("Snapshot current data", BackupJobKind.Backup, node, state.RepositoryKey, safety) };
        if (volume.RestorePreHooks.Count > 0 && volume.Running)
            steps.Add(new ReplaceStep("Restore pre-hook", BackupJobKind.Hook, node, null, new JobParameters { Hooks = volume.RestorePreHooks }));
        if (services.Count > 0)
        {
            steps.Add(new ReplaceStep("Stop services", BackupJobKind.Scale, managerNode, null,
                new JobParameters { Scale = [.. services.Select(s => new ServiceReplicas(s.Name, 0))] }, ScalesDown: true));
        }
        steps.Add(new ReplaceStep("Restore", BackupJobKind.Restore, node, snapshot.RepositoryKey, new JobParameters
        {
            SnapshotId = snapshot.SnapshotId,
            SnapshotPath = SnapshotPath(snapshot),
            RestoreVolume = volume.VolumeName,
            // Deleting what the snapshot lacks is only right when the snapshot covered the whole volume.
            RestoreDelete = snapshot.Paths.Length == 0,
            StopContainers = services.Count == 0,
        }));
        if (services.Count > 0)
        {
            steps.Add(new ReplaceStep("Start services", BackupJobKind.Scale, managerNode, null,
                new JobParameters { Scale = [.. services.Select(s => new ServiceReplicas(s.Name, s.Replicas ?? 1))] }));
        }
        if (volume.RestorePostHooks.Count > 0 && hooksCanRun)
            steps.Add(new ReplaceStep("Restore post-hook", BackupJobKind.Hook, node, null, new JobParameters { Hooks = volume.RestorePostHooks }));

        var parent = NewJob(BackupJobKind.Replace, BackupTrigger.Restore, state, node, snapshot.RepositoryKey, new JobParameters
        {
            SnapshotId = snapshot.SnapshotId,
            Steps = steps,
        });
        parent.Status = BackupJobStatus.Running;
        parent.StartedAt = parent.QueuedAt;
        parent.RequestedBy = requestedBy;
        _context.BackupJobs.Add(parent);
        return parent;
    }

    public BackupJob StepJob(BackupJob parent, ReplaceStep step, int index) => new()
    {
        Kind = step.Kind,
        Status = BackupJobStatus.Queued,
        Trigger = BackupTrigger.Restore,
        VolumeKey = parent.VolumeKey,
        VolumeName = parent.VolumeName,
        NodeName = step.NodeName,
        RepositoryKey = step.RepositoryKey,
        StackName = parent.StackName,
        ParentJobId = parent.Id,
        Step = index,
        QueuedAt = DateTime.UtcNow,
        RequestedBy = parent.RequestedBy,
        ParametersJson = step.Parameters.Serialize(),
    };

    public static string SnapshotPath(BackupSnapshot snapshot) => $"/data/{snapshot.VolumeName}";

    private static JobParameters BackupParameters(BackupVolumeState state, bool preRestore)
    {
        var volume = state.Volume;
        var tags = new List<string> { preRestore ? BackupSnapshot.PreRestoreTag : BackupSnapshot.Tag };
        if (volume.StackName is not null)
            tags.Add($"stack={volume.StackName}");
        if (volume.NodeName is not null)
            tags.Add($"node={volume.NodeName}");

        return new JobParameters
        {
            Host = volume.Key,
            Paths = volume.Paths,
            Tags = tags,
            PreHooks = volume.PreHooks,
            PostHooks = volume.PostHooks,
            Images = volume.Images,
        };
    }

    private static BackupJob NewJob(
        BackupJobKind kind, BackupTrigger trigger, BackupVolumeState state, string? node, string? repositoryKey, JobParameters parameters) => new()
        {
            Kind = kind,
            Status = BackupJobStatus.Queued,
            Trigger = trigger,
            VolumeKey = state.Volume.Key,
            VolumeName = state.Volume.VolumeName,
            NodeName = node,
            RepositoryKey = repositoryKey,
            StackName = state.Volume.StackName,
            QueuedAt = DateTime.UtcNow,
            ParametersJson = parameters.Serialize(),
        };
}
