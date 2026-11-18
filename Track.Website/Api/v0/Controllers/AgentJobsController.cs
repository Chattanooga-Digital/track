using Track.Configuration;
using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Backups;
using Track.Website.Services;
using Track.Website.Services.Backups;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Track.Website.Api.v0.Controllers;

/// <summary>Approved agents fetch and report their jobs here; a repository password is derived only for the job's own node.</summary>
[ApiController]
[Route("/api/v0/agent/jobs")]
public class AgentJobsController(
    IOptions<TrackOptions> options,
    TrackContext trackContext,
    AgentEnrollmentRegistry enrollment,
    ResticTargets resticTargets,
    BackupScheduler scheduler,
    BackupNotifier notifier) : ControllerBase
{
    protected TrackOptions Options { get; } = options.Value;
    protected TrackContext TrackContext { get; } = trackContext;
    protected AgentEnrollmentRegistry Enrollment { get; } = enrollment;
    protected ResticTargets ResticTargets { get; } = resticTargets;
    protected BackupScheduler Scheduler { get; } = scheduler;
    protected BackupNotifier Notifier { get; } = notifier;

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get([FromRoute] long id, CancellationToken cancellationToken)
    {
        if (CallingNode() is not { } node)
            return Unauthorized();

        var job = await FindAsync(id, node, cancellationToken);
        if (job is null)
            return NotFound();

        ResticTarget? restic = null;
        if (job.Kind is BackupJobKind.Backup or BackupJobKind.Restore)
        {
            var target = await TrackContext.BackupTargets.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            try
            {
                restic = target is not null && job.RepositoryKey is not null
                    ? ResticTargets.For(target, job.RepositoryKey, maintenance: false)
                    : throw new InvalidOperationException("No backup target is set");
            }
            catch (InvalidOperationException ex)
            {
                BackupScheduler.Finish(job, BackupJobStatus.Failed, ex.Message);
                await TrackContext.SaveChangesAsync(cancellationToken);
                await Notifier.ChangedAsync(cancellationToken);
                return NotFound();
            }
        }

        var settings = await TrackContext.BackupSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new BackupSettings();
        job.Status = BackupJobStatus.Running;
        job.StartedAt ??= DateTime.UtcNow;
        job.LastProgressAt = DateTime.UtcNow;
        await TrackContext.SaveChangesAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);

        var parameters = JobParameters.Parse(job.ParametersJson);
        return Ok(new AgentJob(
            job.Id,
            job.Kind,
            job.VolumeName,
            parameters.Host,
            restic,
            parameters.Paths ?? [],
            parameters.Tags ?? [],
            parameters.PreHooks ?? [],
            parameters.PostHooks ?? [],
            parameters.Hooks ?? [],
            parameters.SnapshotId,
            parameters.SnapshotPath,
            parameters.RestoreVolume,
            parameters.RestoreDelete,
            parameters.StopContainers,
            parameters.Scale ?? [],
            settings.HookTimeoutMinutes * 60));
    }

    [HttpPost("{id:long}/progress")]
    public async Task<IActionResult> Progress([FromRoute] long id, [FromBody] AgentJobProgress progress, CancellationToken cancellationToken)
    {
        if (CallingNode() is not { } node)
            return Unauthorized();

        var job = await FindAsync(id, node, cancellationToken);
        if (job is null)
            return NotFound();

        job.PercentDone = progress.PercentDone;
        job.BytesDone = progress.BytesDone;
        job.BytesTotal = progress.BytesTotal;
        job.LastProgressAt = DateTime.UtcNow;
        await TrackContext.SaveChangesAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);

        return Ok(new AgentJobProgressResponse(job.CancelRequested));
    }

    [HttpPost("{id:long}/result")]
    public async Task<IActionResult> Result([FromRoute] long id, [FromBody] AgentJobResult result, CancellationToken cancellationToken)
    {
        if (CallingNode() is not { } node)
            return Unauthorized();

        var job = await FindAsync(id, node, cancellationToken);
        if (job is null)
            return NotFound();

        BackupScheduler.Finish(job,
            result.Succeeded ? BackupJobStatus.Succeeded : result.Cancelled ? BackupJobStatus.Cancelled : BackupJobStatus.Failed,
            result.Succeeded ? null : result.Error ?? "Failed");
        job.SnapshotId = result.SnapshotId;
        job.BytesAdded = result.BytesAdded;
        job.FilesNew = result.FilesNew;
        job.FilesChanged = result.FilesChanged;
        job.OutputTail = result.Output;
        if (result.Succeeded)
            job.PercentDone = 1;

        if (job is { Kind: BackupJobKind.Backup, RepositoryKey: not null, VolumeKey: not null, VolumeName: not null }
            && result is { Succeeded: true, SnapshotId: not null }
            && !await TrackContext.BackupSnapshots.AnyAsync(s => s.RepositoryKey == job.RepositoryKey && s.SnapshotId == result.SnapshotId, cancellationToken))
        {
            var parameters = JobParameters.Parse(job.ParametersJson);
            TrackContext.BackupSnapshots.Add(new BackupSnapshot
            {
                SnapshotId = result.SnapshotId,
                RepositoryKey = job.RepositoryKey,
                VolumeKey = job.VolumeKey,
                VolumeName = job.VolumeName,
                Time = job.StartedAt ?? DateTime.UtcNow,
                NodeName = job.NodeName,
                StackName = job.StackName,
                Images = string.Join(',', parameters.Images ?? []),
                Paths = string.Join(',', parameters.Paths ?? []),
                BytesAdded = result.BytesAdded,
                TotalBytes = result.TotalBytes,
                PreRestore = parameters.Tags?.Contains(BackupSnapshot.PreRestoreTag) == true,
                JobId = job.Id,
            });
        }

        await TrackContext.SaveChangesAsync(cancellationToken);

        // A restore's next step shouldn't wait for the scheduler's next pass while services are down.
        if (job.ParentJobId is not null)
            await Scheduler.TickAsync(cancellationToken);
        else
            await Notifier.ChangedAsync(cancellationToken);

        return Ok();
    }

    private string? CallingNode() =>
        IngestKey.IsValid(Request, Options) ? Enrollment.ApprovedNodeFor(IngestKey.AgentToken(Request)) : null;

    private async Task<BackupJob?> FindAsync(long id, string node, CancellationToken cancellationToken)
    {
        var job = await TrackContext.BackupJobs.SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        return job is not null && job.NodeName == node && job.Status is BackupJobStatus.Dispatched or BackupJobStatus.Running
            && BackupScheduler.AgentKinds.Contains(job.Kind)
            ? job
            : null;
    }
}
