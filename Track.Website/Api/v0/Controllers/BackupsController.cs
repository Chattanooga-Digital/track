using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Backups;
using Track.Website.Api.v0.Models.Backups;
using Track.Website.Services.Backups;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Track.Website.Api.v0.Controllers;

[ApiController]
[Authorize]
[Route("/api/v0/backups")]
public class BackupsController(
    TrackContext trackContext,
    BackupCatalogService catalogService,
    BackupJobFactory jobFactory,
    BackupScheduler scheduler,
    BackupSecretProtector secretProtector,
    BackupMasterKey masterKey,
    BackupTargetTester targetTester,
    ResticTargets resticTargets,
    ResticRunner restic,
    BackupRuntimeState runtime,
    BackupNotifier notifier) : ControllerBase
{
    private const int MaxJobs = 300;

    protected TrackContext TrackContext { get; } = trackContext;
    protected BackupCatalogService CatalogService { get; } = catalogService;
    protected BackupJobFactory JobFactory { get; } = jobFactory;
    protected BackupScheduler Scheduler { get; } = scheduler;
    protected BackupSecretProtector SecretProtector { get; } = secretProtector;
    protected BackupMasterKey MasterKey { get; } = masterKey;
    protected BackupTargetTester TargetTester { get; } = targetTester;
    protected ResticTargets ResticTargets { get; } = resticTargets;
    protected ResticRunner Restic { get; } = restic;
    protected BackupRuntimeState Runtime { get; } = runtime;
    protected BackupNotifier Notifier { get; } = notifier;

    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        var catalog = await CatalogService.BuildAsync(cancellationToken);
        var repositories = await TrackContext.BackupRepositories.AsNoTracking().ToDictionaryAsync(r => r.Key, cancellationToken);
        var since = DateTime.UtcNow.AddHours(-24);
        var uploaded = await TrackContext.BackupJobs.AsNoTracking()
            .Where(j => j.Kind == BackupJobKind.Backup && j.Status == BackupJobStatus.Succeeded && j.FinishedAt >= since)
            .SumAsync(j => j.BytesAdded ?? 0, cancellationToken);

        var groups = catalog.Volumes
            .GroupBy(v => v.RepositoryKey)
            .Select(g => new BackupGroupModel
            {
                CustomerId = g.First().CustomerId,
                CustomerName = g.First().CustomerName,
                IsShared = g.First().IsShared,
                RepositoryKey = g.Key,
                RepositorySizeBytes = repositories.GetValueOrDefault(g.Key)?.SizeBytes,
                Volumes = [.. g.OrderBy(v => v.Volume.VolumeName).ThenBy(v => v.Volume.NodeName).Select(ToModel)],
            })
            .OrderBy(g => g.CustomerId is null ? 1 : 0)
            .ThenBy(g => g.IsShared ? 0 : 1)
            .ThenBy(g => g.CustomerName)
            .ToList();

        return Ok(new BackupOverviewModel
        {
            TargetSet = catalog.Target is not null,
            MasterKeySet = catalog.MasterKeySet,
            Paused = catalog.Settings.Paused,
            Included = catalog.Volumes.Count(v => v.Volume.Protected),
            Protected = catalog.Volumes.Count(v => v.Volume.Protected && v.LastGoodAt > DateTime.UtcNow.AddHours(-catalog.Settings.StaleAfterHours)),
            NeedsAttention = catalog.Volumes.Count(v => v.NeedsAttention) + catalog.AnonymousDatabases.Count,
            Ignored = catalog.Volumes.Count(v => v.Status == BackupVolumeStatus.Excluded),
            UploadedLast24HoursBytes = uploaded,
            ReadyNodes = [.. catalog.ReadyNodes.Order()],
            Groups = groups,
            AnonymousDatabases = catalog.AnonymousDatabases,
        });
    }

    [HttpPut("volumes/override")]
    public async Task<IActionResult> SetOverride([FromBody] VolumeOverrideModel model, CancellationToken cancellationToken)
    {
        var names = model.VolumeNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        if (names.Count == 0)
            return BadRequest("Pick at least one volume.");

        var existing = await TrackContext.BackupVolumeOverrides
            .Where(o => names.Contains(o.VolumeName))
            .ToDictionaryAsync(o => o.VolumeName, cancellationToken);
        foreach (var name in names)
            SetChoice(existing.GetValueOrDefault(name), name, choice => choice.Excluded = model.Excluded);

        await TrackContext.SaveChangesAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("volumes/method")]
    public async Task<IActionResult> SetMethod([FromBody] VolumeMethodModel model, CancellationToken cancellationToken)
    {
        var existing = await TrackContext.BackupVolumeOverrides.SingleOrDefaultAsync(o => o.VolumeName == model.VolumeName, cancellationToken);
        SetChoice(existing, model.VolumeName, choice => choice.Method = model.Method);

        await TrackContext.SaveChangesAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("volumes/test")]
    public async Task<IActionResult> TestRestore([FromBody] VolumeTestModel model, CancellationToken cancellationToken)
    {
        var catalog = await CatalogService.BuildAsync(cancellationToken);
        if (!catalog.Configured)
            return BadRequest("Set up the backup target and master key first.");

        var state = catalog.Volumes.FirstOrDefault(v => v.Volume.Key == model.Key);
        if (state is null)
            return BadRequest("That volume isn't known.");
        if (state.ActiveJob is not null)
            return Conflict("That volume already has a job in progress.");

        var snapshots = TrackContext.BackupSnapshots.AsNoTracking()
            .Where(s => s.VolumeName == state.Volume.VolumeName && s.DumpEngine != null);
        var snapshot = model.SnapshotId is { } id
            ? await snapshots.SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            : await snapshots.Where(s => s.VolumeKey == state.Volume.Key).OrderByDescending(s => s.Time).FirstOrDefaultAsync(cancellationToken);
        if (snapshot is null)
            return BadRequest("There's no database dump to test yet.");

        var image = Split(snapshot.Images).FirstOrDefault(i => DatabaseDumps.EngineFor(i) == snapshot.DumpEngine)
            ?? (state.Volume.Engine == snapshot.DumpEngine ? state.Volume.DatabaseImage : null);
        if (image is null)
            return BadRequest("Can't tell which database image the dump came from.");

        var node = state.Volume.NodeName is { } own && catalog.ReadyNodes.Contains(own) ? own : catalog.ReadyNodes.Order().FirstOrDefault();
        if (node is null)
            return BadRequest("No approved agent is online.");

        JobFactory.QueueVerify(snapshot, image, node, User.Identity?.Name);
        await TrackContext.SaveChangesAsync(cancellationToken);
        await Scheduler.TickAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("volumes/backup")]
    public async Task<IActionResult> BackupNow([FromBody] VolumeKeyModel model, CancellationToken cancellationToken)
    {
        var catalog = await CatalogService.BuildAsync(cancellationToken);
        if (!catalog.Configured)
            return BadRequest("Set up the backup target and master key first.");

        var state = catalog.Volumes.FirstOrDefault(v => v.Volume.Key == model.Key);
        if (state is null || !state.Volume.Protected)
            return BadRequest("That volume isn't backed up.");
        if (state.ActiveJob is not null)
            return Conflict("That volume already has a job in progress.");

        await JobFactory.QueueBackupAsync(state, BackupTrigger.Manual, User.Identity?.Name, cancellationToken);
        await TrackContext.SaveChangesAsync(cancellationToken);
        await Scheduler.TickAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("volumes/backup-all")]
    public async Task<IActionResult> BackupAll(CancellationToken cancellationToken)
    {
        var catalog = await CatalogService.BuildAsync(cancellationToken);
        if (!catalog.Configured)
            return BadRequest("Set up the backup target and master key first.");

        foreach (var state in catalog.Volumes.Where(v => v.Volume.Protected && v.ActiveJob is null))
            await JobFactory.QueueBackupAsync(state, BackupTrigger.Manual, User.Identity?.Name, cancellationToken);

        await TrackContext.SaveChangesAsync(cancellationToken);
        await Scheduler.TickAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("snapshots")]
    public async Task<IActionResult> Snapshots([FromQuery] string volume, CancellationToken cancellationToken)
    {
        var catalog = await CatalogService.BuildAsync(cancellationToken);
        var currentImages = catalog.Volumes
            .Where(v => v.Volume.VolumeName == volume)
            .SelectMany(v => v.Volume.Images)
            .Select(ImageName)
            .ToHashSet();

        var snapshots = await TrackContext.BackupSnapshots.AsNoTracking()
            .Where(s => s.VolumeName == volume)
            .OrderByDescending(s => s.Time)
            .ToListAsync(cancellationToken);

        return Ok(snapshots.Select(s =>
        {
            var images = Split(s.Images);
            return new BackupSnapshotModel
            {
                Id = s.Id,
                SnapshotId = s.SnapshotId,
                RepositoryKey = s.RepositoryKey,
                VolumeKey = s.VolumeKey,
                VolumeName = s.VolumeName,
                Time = s.Time,
                NodeName = s.NodeName,
                Images = images,
                Paths = Split(s.Paths),
                BytesAdded = s.BytesAdded,
                TotalBytes = s.TotalBytes,
                PreRestore = s.PreRestore,
                DumpEngine = s.DumpEngine,
                DifferentImage = images.Count > 0 && currentImages.Count > 0 && !images.Select(ImageName).ToHashSet().SetEquals(currentImages),
            };
        }));
    }

    [HttpGet("snapshots/{id:long}/files")]
    public async Task<IActionResult> Files([FromRoute] long id, [FromQuery] string? path, CancellationToken cancellationToken)
    {
        var snapshot = await TrackContext.BackupSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        var target = await TrackContext.BackupTargets.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (snapshot is null)
            return NotFound();
        if (target is null || MasterKey.Value is null)
            return BadRequest("Set up the backup target and master key first.");
        if (CleanPath(path) is not { } relative)
            return BadRequest("That path isn't valid.");

        var directory = InSnapshot(snapshot, relative);
        var result = await Restic.RunAsync(ResticTargets.For(target, snapshot.RepositoryKey, maintenance: true),
            ["ls", "--json", snapshot.SnapshotId, directory], cancellationToken);
        if (!result.Succeeded)
            return BadRequest(result.Message);

        var entries = new List<SnapshotEntryModel>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            var node = document.RootElement;
            if (!node.TryGetProperty("struct_type", out var type) || type.GetString() != "node"
                || node.GetProperty("path").GetString() is not { } entryPath
                || entryPath == directory || entryPath[..entryPath.LastIndexOf('/')] != directory)
                continue;

            entries.Add(new SnapshotEntryModel
            {
                Name = node.GetProperty("name").GetString()!,
                IsDirectory = node.GetProperty("type").GetString() == "dir",
                Size = node.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : null,
                ModifiedAt = node.TryGetProperty("mtime", out var mtime) ? SnapshotIndex.ParseTime(mtime.GetString()) : null,
            });
        }

        return Ok(new SnapshotListingModel
        {
            Path = relative,
            Entries = [.. entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)],
        });
    }

    /// <summary>The whole snapshot, or one file or folder in it when <paramref name="path"/> is given.</summary>
    [HttpGet("snapshots/{id:long}/download")]
    public async Task<IActionResult> Download(
        [FromRoute] long id, [FromQuery] string? path, [FromQuery] bool directory, CancellationToken cancellationToken)
    {
        var snapshot = await TrackContext.BackupSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        var target = await TrackContext.BackupTargets.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (snapshot is null)
            return NotFound();
        if (target is null || MasterKey.Value is null)
            return BadRequest("Set up the backup target and master key first.");
        if (CleanPath(path) is not { } relative)
            return BadRequest("That path isn't valid.");

        var stamp = $"{snapshot.VolumeName}-{snapshot.Time:yyyyMMdd-HHmm}";
        string source;
        string fileName;
        bool archive;
        if (relative.Length > 0)
        {
            source = InSnapshot(snapshot, relative);
            archive = directory;
            fileName = relative[(relative.LastIndexOf('/') + 1)..] + (archive ? ".tar" : "");
        }
        else if (snapshot.DumpEngine is not null)
        {
            source = BackupJobFactory.DumpPath(snapshot.VolumeName);
            archive = false;
            fileName = $"{stamp}.sql";
        }
        else
        {
            source = BackupJobFactory.SnapshotPath(snapshot);
            archive = true;
            fileName = $"{stamp}.tar";
        }

        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        Response.ContentType = archive ? "application/x-tar" : "application/octet-stream";
        Response.Headers.ContentDisposition = disposition.ToString();
        await Restic.RunAsync(ResticTargets.For(target, snapshot.RepositoryKey, maintenance: true),
            archive ? ["dump", "--archive", "tar", snapshot.SnapshotId, source] : ["dump", snapshot.SnapshotId, source],
            cancellationToken, Response.Body);
        return new EmptyResult();
    }

    [HttpPost("restore")]
    public async Task<IActionResult> Restore([FromBody] RestoreRequestModel model, CancellationToken cancellationToken)
    {
        var snapshot = await TrackContext.BackupSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == model.SnapshotId, cancellationToken);
        if (snapshot is null)
            return NotFound();

        var catalog = await CatalogService.BuildAsync(cancellationToken);
        if (!catalog.Configured)
            return BadRequest("Set up the backup target and master key first.");

        if (snapshot.DumpEngine is not null && model.Mode == RestoreMode.NewVolume)
            return BadRequest("A database dump restores into the database: replace the live data or download it.");

        if (model.Mode == RestoreMode.NewVolume)
        {
            var current = catalog.Volumes.FirstOrDefault(v => v.Volume.Key == snapshot.VolumeKey);
            var node = model.NodeName ?? current?.Volume.NodeName ?? snapshot.NodeName;
            if (node is null || !catalog.ReadyNodes.Contains(node))
                return BadRequest("Pick an online, approved node to restore onto.");

            var job = JobFactory.QueueRestoreToNewVolume(snapshot, node, User.Identity?.Name);
            await TrackContext.SaveChangesAsync(cancellationToken);
            await Scheduler.TickAsync(cancellationToken);
            return Ok(new { job.Id, Volume = JobParameters.Parse(job.ParametersJson).RestoreVolume });
        }

        if (!User.IsInRole(Role.Administrator))
            return Forbid();

        var state = catalog.Volumes.FirstOrDefault(v => v.Volume.Key == snapshot.VolumeKey && v.Volume.Kind == CatalogVolumeKind.Single)
            ?? catalog.Volumes.FirstOrDefault(v => v.Volume.VolumeName == snapshot.VolumeName && v.Volume.Kind == CatalogVolumeKind.Single);
        if (state is null)
            return BadRequest("That volume isn't on any node right now.");
        if (model.ConfirmVolumeName != state.Volume.VolumeName)
            return BadRequest("Type the volume name to confirm.");
        if ((snapshot.DumpEngine is null ? state.ReplaceBlockedReason : state.DumpReplaceBlockedReason) is { } reason)
            return BadRequest(reason);
        if (snapshot.DumpEngine is { } engine && state.Volume.Engine != engine)
            return BadRequest($"The dump is from {engine}, but {state.Volume.DatabaseService} isn't.");
        if (state.ActiveJob is not null)
            return Conflict("That volume already has a job in progress.");

        var manager = catalog.Swarm is not null && catalog.ReadyNodes.Contains(catalog.Swarm.NodeName) ? catalog.Swarm.NodeName : null;
        var parent = await JobFactory.QueueReplaceAsync(state, snapshot, catalog.Swarm, manager, User.Identity?.Name, cancellationToken);
        await TrackContext.SaveChangesAsync(cancellationToken);
        await Scheduler.TickAsync(cancellationToken);
        return Ok(new { parent.Id });
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs(CancellationToken cancellationToken)
    {
        var jobs = await TrackContext.BackupJobs.AsNoTracking()
            .OrderByDescending(j => j.Id)
            .Take(MaxJobs)
            .ToListAsync(cancellationToken);
        var stepNames = StepNames(jobs);
        return Ok(jobs.Select(j => ToModel(j, stepNames, includeOutput: false)));
    }

    [HttpGet("jobs/{id:long}")]
    public async Task<IActionResult> Job([FromRoute] long id, CancellationToken cancellationToken)
    {
        var job = await TrackContext.BackupJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
            return NotFound();
        var parent = job.ParentJobId is { } parentId
            ? await TrackContext.BackupJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == parentId, cancellationToken)
            : null;
        return Ok(ToModel(job, StepNames(parent is null ? [job] : [job, parent]), includeOutput: true));
    }

    [HttpPost("jobs/{id:long}/cancel")]
    public async Task<IActionResult> Cancel([FromRoute] long id, CancellationToken cancellationToken)
    {
        var job = await TrackContext.BackupJobs.SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
            return NotFound();
        if (!job.IsActive)
            return Conflict("That job has already finished.");

        if (job.Status == BackupJobStatus.Queued && job.Kind != BackupJobKind.Replace)
            BackupScheduler.Finish(job, BackupJobStatus.Cancelled, "Cancelled");
        else
            job.CancelRequested = true;

        await TrackContext.SaveChangesAsync(cancellationToken);
        await Scheduler.TickAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await CatalogService.SettingsAsync(cancellationToken);
        return Ok(new BackupSettingsModel
        {
            Paused = settings.Paused,
            BackupMinuteUtc = settings.BackupMinuteUtc,
            SpreadMinutes = settings.SpreadMinutes,
            MaxJobsPerNode = settings.MaxJobsPerNode,
            MaxJobsTotal = settings.MaxJobsTotal,
            KeepDaily = settings.KeepDaily,
            KeepWeekly = settings.KeepWeekly,
            KeepMonthly = settings.KeepMonthly,
            PruneDay = settings.PruneDay,
            PruneMinuteUtc = settings.PruneMinuteUtc,
            CheckPercent = settings.CheckPercent,
            StaleAfterHours = settings.StaleAfterHours,
            HookTimeoutMinutes = settings.HookTimeoutMinutes,
            HeartbeatUrl = settings.HeartbeatUrl,
        });
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] BackupSettingsModel model, CancellationToken cancellationToken)
    {
        if (model.KeepDaily < 1 || model.KeepWeekly < 0 || model.KeepMonthly < 0)
            return BadRequest("Keep at least one daily snapshot.");
        if (model.MaxJobsPerNode < 1 || model.MaxJobsTotal < 1)
            return BadRequest("Allow at least one job at a time.");
        if (model.BackupMinuteUtc is < 0 or >= 1440 || model.PruneMinuteUtc is < 0 or >= 1440)
            return BadRequest("Times must be within the day.");
        if (!string.IsNullOrWhiteSpace(model.HeartbeatUrl) && !Uri.TryCreate(model.HeartbeatUrl.Trim(), UriKind.Absolute, out _))
            return BadRequest("The heartbeat URL isn't a valid URL.");

        var settings = await TrackContext.BackupSettings.SingleOrDefaultAsync(cancellationToken)
            ?? TrackContext.BackupSettings.Add(new BackupSettings()).Entity;
        settings.Paused = model.Paused;
        settings.BackupMinuteUtc = model.BackupMinuteUtc;
        settings.SpreadMinutes = Math.Clamp(model.SpreadMinutes, 0, 1440);
        settings.MaxJobsPerNode = model.MaxJobsPerNode;
        settings.MaxJobsTotal = model.MaxJobsTotal;
        settings.KeepDaily = model.KeepDaily;
        settings.KeepWeekly = model.KeepWeekly;
        settings.KeepMonthly = model.KeepMonthly;
        settings.PruneDay = model.PruneDay;
        settings.PruneMinuteUtc = model.PruneMinuteUtc;
        settings.CheckPercent = Math.Clamp(model.CheckPercent, 1, 100);
        settings.StaleAfterHours = Math.Max(1, model.StaleAfterHours);
        settings.HookTimeoutMinutes = Math.Clamp(model.HookTimeoutMinutes, 1, 240);
        settings.HeartbeatUrl = string.IsNullOrWhiteSpace(model.HeartbeatUrl) ? null : model.HeartbeatUrl.Trim();
        settings.UpdatedAt = DateTime.UtcNow;
        await TrackContext.SaveChangesAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("target")]
    public async Task<IActionResult> GetTarget(CancellationToken cancellationToken)
    {
        var target = await TrackContext.BackupTargets.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var repositories = await TrackContext.BackupRepositories.AsNoTracking()
            .OrderBy(r => r.Key)
            .Select(r => new BackupRepositoryModel
            {
                Key = r.Key,
                CustomerName = r.CustomerName,
                IsShared = r.IsShared,
                SizeBytes = r.SizeBytes,
                LastPrunedAt = r.LastPrunedAt,
                LastCheckedAt = r.LastCheckedAt,
                LastMaintenanceError = r.LastMaintenanceError,
            })
            .ToListAsync(cancellationToken);

        var model = new BackupTargetModel
        {
            Configured = target is not null,
            MasterKeySet = MasterKey.Value is not null,
            MasterKeyFromEnvironment = MasterKey.FromEnvironment,
            MasterKeySecretName = MasterKey.SecretName,
            Repositories = repositories,
        };
        if (target is not null)
        {
            model.Endpoint = target.Endpoint;
            model.Region = target.Region;
            model.Bucket = target.Bucket;
            model.Prefix = target.Prefix;
            model.Addressing = target.Addressing;
            model.AgentAccessKey = target.AgentAccessKey;
            model.HasAgentSecret = target.AgentSecretKeyProtected.Length > 0;
            model.MaintenanceAccessKey = target.MaintenanceAccessKey;
            model.HasMaintenanceSecret = !string.IsNullOrEmpty(target.MaintenanceSecretKeyProtected);
        }

        return Ok(model);
    }

    [HttpPut("target")]
    [Authorize(Roles = Role.Administrator)]
    public async Task<IActionResult> UpdateTarget([FromBody] BackupTargetEditModel model, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(model.Endpoint.Trim(), UriKind.Absolute, out _))
            return BadRequest("The endpoint must be a full URL, such as https://s3.example.com.");
        if (string.IsNullOrWhiteSpace(model.Bucket) || string.IsNullOrWhiteSpace(model.AgentAccessKey))
            return BadRequest("Bucket and agent access key are required.");

        var target = await TrackContext.BackupTargets.SingleOrDefaultAsync(cancellationToken);
        if (target is null && string.IsNullOrEmpty(model.AgentSecretKey))
            return BadRequest("Agent secret key is required.");

        // Every repository's location depends on these, so moving them after backups exist would orphan the snapshots.
        if (target is not null && await TrackContext.BackupRepositories.AnyAsync(r => r.InitializedAt != null, cancellationToken)
            && (target.Endpoint != model.Endpoint.Trim() || target.Bucket != model.Bucket.Trim() || target.Prefix != Prefix(model.Prefix)))
            return BadRequest("Endpoint, bucket and prefix can't change once backups exist there.");

        target ??= TrackContext.BackupTargets.Add(new BackupTarget
        {
            Endpoint = "",
            Bucket = "",
            AgentAccessKey = "",
            AgentSecretKeyProtected = "",
        }).Entity;
        target.Endpoint = model.Endpoint.Trim();
        target.Region = string.IsNullOrWhiteSpace(model.Region) ? "us-east-1" : model.Region.Trim();
        target.Bucket = model.Bucket.Trim();
        target.Prefix = Prefix(model.Prefix);
        target.Addressing = model.Addressing;
        target.AgentAccessKey = model.AgentAccessKey.Trim();
        if (!string.IsNullOrEmpty(model.AgentSecretKey))
            target.AgentSecretKeyProtected = SecretProtector.Protect(model.AgentSecretKey);
        target.MaintenanceAccessKey = string.IsNullOrWhiteSpace(model.MaintenanceAccessKey) ? null : model.MaintenanceAccessKey.Trim();
        if (target.MaintenanceAccessKey is null)
            target.MaintenanceSecretKeyProtected = null;
        else if (!string.IsNullOrEmpty(model.MaintenanceSecretKey))
            target.MaintenanceSecretKeyProtected = SecretProtector.Protect(model.MaintenanceSecretKey);
        target.UpdatedAt = DateTime.UtcNow;

        await TrackContext.SaveChangesAsync(cancellationToken);
        await Notifier.ChangedAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("target/test")]
    public async Task<IActionResult> TestTarget([FromBody] BackupTargetEditModel model, CancellationToken cancellationToken)
    {
        var secret = model.AgentSecretKey;
        if (string.IsNullOrEmpty(secret))
        {
            var stored = await TrackContext.BackupTargets.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (stored is not null)
                secret = SecretProtector.Unprotect(stored.AgentSecretKeyProtected);
        }
        if (string.IsNullOrEmpty(secret))
            return BadRequest("Agent secret key is required.");

        var checks = await TargetTester.TestAsync(
            model.Endpoint.Trim(),
            string.IsNullOrWhiteSpace(model.Region) ? "us-east-1" : model.Region.Trim(),
            model.Bucket.Trim(),
            Prefix(model.Prefix),
            model.Addressing,
            model.AgentAccessKey.Trim(),
            secret,
            cancellationToken);
        return Ok(checks);
    }

    [HttpPost("maintenance")]
    public IActionResult RunMaintenance()
    {
        Runtime.MaintenanceRequested = true;
        return Accepted();
    }

    private static BackupVolumeModel ToModel(BackupVolumeState state)
    {
        var volume = state.Volume;
        return new BackupVolumeModel
        {
            Key = volume.Key,
            VolumeName = volume.VolumeName,
            NodeName = volume.NodeName,
            StackName = volume.StackName,
            Services = volume.Services,
            Images = volume.Images,
            Copies = volume.Copies,
            SizeBytes = volume.SizeBytes,
            Status = state.Status,
            ExcludedBy = volume.ExcludedBy,
            Override = volume.Override,
            HasHooks = volume.Method == BackupMethod.Files && volume.PreHooks.Count > 0,
            HasRestoreHooks = volume.RestorePreHooks.Count > 0 || volume.RestorePostHooks.Count > 0,
            DatabaseWithoutDump = volume.Protected && volume.DatabaseWithoutDump,
            Method = volume.Method,
            MethodOverride = volume.MethodOverride,
            Engine = volume.Engine,
            LastTest = state.LastTest is { } test
                ? new BackupTestModel
                {
                    JobId = test.Id,
                    FinishedAt = test.FinishedAt,
                    Succeeded = test.Status == BackupJobStatus.Succeeded,
                    Message = test.Status == BackupJobStatus.Succeeded ? test.Summary : test.Error,
                }
                : null,
            Paths = volume.Paths,
            LastGoodAt = state.LastGoodAt,
            ActiveJobId = state.ActiveJob?.Id,
            ActiveJobKind = state.ActiveJob?.Kind,
            PercentDone = state.ActiveJob?.PercentDone,
            LastError = state.Status == BackupVolumeStatus.Failed ? state.LastJob?.Error : null,
            ReplaceBlockedReason = state.ReplaceBlockedReason,
            DumpReplaceBlockedReason = state.DumpReplaceBlockedReason,
        };
    }

    private void SetChoice(BackupVolumeOverride? existing, string volumeName, Action<BackupVolumeOverride> apply)
    {
        existing ??= TrackContext.BackupVolumeOverrides.Add(new BackupVolumeOverride { VolumeName = volumeName }).Entity;
        apply(existing);
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = User.Identity?.Name;
        if (existing.Excluded is null && existing.Method is null)
            TrackContext.BackupVolumeOverrides.Remove(existing);
    }

    /// <summary>A path inside a volume with no <c>.</c> or <c>..</c> parts; empty for the root, null when it isn't valid.</summary>
    private static string? CleanPath(string? path)
    {
        var parts = (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => p is "." or "..") ? null : string.Join('/', parts);
    }

    private static string InSnapshot(BackupSnapshot snapshot, string relative) =>
        relative.Length == 0 ? $"/data/{snapshot.VolumeName}" : $"/data/{snapshot.VolumeName}/{relative}";

    private static BackupJobModel ToModel(BackupJob job, IReadOnlyDictionary<long, List<ReplaceStep>> stepsByParent, bool includeOutput) => new()
    {
        Id = job.Id,
        Kind = job.Kind,
        Status = job.Status,
        Trigger = job.Trigger,
        VolumeKey = job.VolumeKey,
        VolumeName = job.VolumeName,
        NodeName = job.NodeName,
        RepositoryKey = job.RepositoryKey,
        ParentJobId = job.ParentJobId,
        StepName = job.ParentJobId is { } parent && stepsByParent.TryGetValue(parent, out var steps) && job.Step < steps.Count
            ? steps[job.Step].Name
            : null,
        QueuedAt = job.QueuedAt,
        StartedAt = job.StartedAt,
        FinishedAt = job.FinishedAt,
        PercentDone = job.PercentDone,
        BytesDone = job.BytesDone,
        BytesTotal = job.BytesTotal,
        BytesAdded = job.BytesAdded,
        FilesNew = job.FilesNew,
        FilesChanged = job.FilesChanged,
        SnapshotId = job.SnapshotId,
        Error = job.Error,
        Summary = job.Summary,
        RequestedBy = job.RequestedBy,
        CancelRequested = job.CancelRequested,
        Output = includeOutput ? job.OutputTail : null,
    };

    private static Dictionary<long, List<ReplaceStep>> StepNames(IEnumerable<BackupJob> jobs) =>
        jobs.Where(j => j.Kind == BackupJobKind.Replace)
            .ToDictionary(j => j.Id, j => JobParameters.Parse(j.ParametersJson).Steps ?? []);

    private static List<string> Split(string? value) =>
        string.IsNullOrEmpty(value) ? [] : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries)];

    private static string ImageName(string image) => image.Split('@')[0];

    private static string Prefix(string? prefix)
    {
        var trimmed = (prefix ?? "").Trim().Trim('/');
        return trimmed.Length == 0 ? "" : trimmed + "/";
    }
}
