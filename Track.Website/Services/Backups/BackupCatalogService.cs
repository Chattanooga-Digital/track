using Track.Configuration;
using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services;
using Track.Services.Backups;
using Track.Services.Ingest;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Track.Website.Services.Backups;

public sealed record BackupVolumeState(
    CatalogVolume Volume,
    BackupVolumeStatus Status,
    string RepositoryKey,
    int? CustomerId,
    string? CustomerName,
    bool IsShared,
    DateTime? LastGoodAt,
    BackupJob? ActiveJob,
    BackupJob? LastJob,
    bool NodeOnline,
    string? ReplaceBlockedReason)
{
    public bool NeedsAttention =>
        Status is BackupVolumeStatus.Stale or BackupVolumeStatus.Failed or BackupVolumeStatus.Never
            or BackupVolumeStatus.StaleCopy or BackupVolumeStatus.Ambiguous or BackupVolumeStatus.NodeOffline
        || (Volume.Protected && Volume.DatabaseWithoutHook);
}

public sealed record BackupCatalogResult(
    List<BackupVolumeState> Volumes,
    BackupSettings Settings,
    BackupTarget? Target,
    bool MasterKeySet,
    SwarmReport? Swarm,
    IReadOnlySet<string> ReadyNodes)
{
    public bool Configured => Target is not null && MasterKeySet;
}

/// <summary>Central: the volume catalog joined with owners, jobs and snapshots, which the page and the scheduler share.</summary>
public sealed class BackupCatalogService(
    TrackContext context,
    VolumeInventoryHolder volumeInventory,
    SwarmInventoryHolder swarmInventory,
    CurrentStateHolder currentState,
    AgentEnrollmentRegistry enrollment,
    BackupMasterKey masterKey,
    IOptions<TrackOptions> options)
{
    private static readonly TimeSpan JobHistory = TimeSpan.FromDays(14);

    private readonly TrackContext _context = context;
    private readonly VolumeInventoryHolder _volumeInventory = volumeInventory;
    private readonly SwarmInventoryHolder _swarmInventory = swarmInventory;
    private readonly CurrentStateHolder _currentState = currentState;
    private readonly AgentEnrollmentRegistry _enrollment = enrollment;
    private readonly BackupMasterKey _masterKey = masterKey;
    private readonly TrackOptions _options = options.Value;

    public async Task<BackupSettings> SettingsAsync(CancellationToken cancellationToken) =>
        await _context.BackupSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new BackupSettings();

    public async Task<BackupCatalogResult> BuildAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var nodes = _currentState.GetCurrent().Nodes;
        var online = nodes.Where(n => n.Online).Select(n => n.Name).ToHashSet();
        var roles = nodes.ToDictionary(n => n.Name, n => n.Role);
        var ready = online.Where(_enrollment.IsApproved).ToHashSet();
        var (volumesByNode, mountsByNode) = _volumeInventory.Get();
        var swarm = _swarmInventory.Get(now);

        var overrides = await _context.BackupVolumeOverrides.AsNoTracking()
            .ToDictionaryAsync(o => o.VolumeName, o => o.Excluded, cancellationToken);

        // Only approved agents decide what exists where: their reports choose which commands run on which node.
        var catalog = BackupCatalog.Build(new CatalogInputs(
            volumesByNode.Where(e => _enrollment.IsApproved(e.Key)).ToDictionary(),
            mountsByNode.Where(e => ready.Contains(e.Key)).ToDictionary(),
            swarm is not null && _enrollment.IsApproved(swarm.NodeName) ? swarm : null,
            overrides,
            new HashSet<string> { _options.Backups.CacheVolume }));

        var settings = await SettingsAsync(cancellationToken);
        var target = await _context.BackupTargets.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        var customers = await _context.Customers.AsNoTracking().Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var customerByStack = await _context.StackAssignments.AsNoTracking()
            .Select(a => new { a.StackName, a.CustomerId })
            .ToListAsync(cancellationToken);
        var customerIdByStack = customerByStack.ToDictionary(a => a.StackName, a => a.CustomerId, StringComparer.OrdinalIgnoreCase);
        var sharedStacks = (await _context.SharedStacks.AsNoTracking().Select(s => s.StackName).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var lastGood = await _context.BackupSnapshots.AsNoTracking()
            .GroupBy(s => s.VolumeKey)
            .Select(g => new { g.Key, Time = g.Max(s => s.Time) })
            .ToDictionaryAsync(g => g.Key, g => g.Time, cancellationToken);

        var since = now - JobHistory;
        var jobs = await _context.BackupJobs.AsNoTracking()
            .Where(j => (j.Kind == BackupJobKind.Backup || j.Kind == BackupJobKind.Restore) && j.VolumeKey != null
                && (j.QueuedAt >= since || j.Status == BackupJobStatus.Queued || j.Status == BackupJobStatus.Dispatched || j.Status == BackupJobStatus.Running))
            .OrderByDescending(j => j.QueuedAt)
            .ToListAsync(cancellationToken);
        var jobsByKey = jobs.GroupBy(j => j.VolumeKey!).ToDictionary(g => g.Key, g => g.ToList());

        var states = catalog.Select(volume =>
        {
            var (repositoryKey, customerId, isShared) = volume.StackName switch
            {
                not null when customerIdByStack.TryGetValue(volume.StackName, out var id) => (BackupRepository.CustomerKey(id), (int?)id, false),
                not null when sharedStacks.Contains(volume.StackName) => (BackupRepository.SharedKey, null, true),
                _ => (BackupRepository.UnassignedKey, null, false),
            };

            var volumeJobs = jobsByKey.GetValueOrDefault(volume.Key) ?? [];
            var active = volumeJobs.FirstOrDefault(j => j.IsActive);
            var last = volumeJobs.FirstOrDefault(j => j.Kind == BackupJobKind.Backup && !j.IsActive && j.Status != BackupJobStatus.Cancelled);
            var good = lastGood.TryGetValue(volume.Key, out var time) ? time : (DateTime?)null;
            var nodeOnline = volume.NodeName is not null && online.Contains(volume.NodeName);

            return new BackupVolumeState(
                volume,
                Status(volume, active, last, good, nodeOnline, settings, now),
                repositoryKey,
                customerId,
                customerId is { } cid ? customers.GetValueOrDefault(cid) : null,
                isShared,
                good,
                active,
                last,
                nodeOnline,
                ReplaceBlockedReason(volume, swarm, ready, roles));
        }).ToList();

        return new BackupCatalogResult(states, settings, target, _masterKey.Value is not null, swarm, ready);
    }

    private static BackupVolumeStatus Status(
        CatalogVolume volume, BackupJob? active, BackupJob? last, DateTime? lastGood, bool nodeOnline, BackupSettings settings, DateTime now)
    {
        if (volume.Kind == CatalogVolumeKind.StaleCopy)
            return BackupVolumeStatus.StaleCopy;
        if (volume.Excluded)
            return BackupVolumeStatus.Excluded;
        if (volume.Kind == CatalogVolumeKind.Ambiguous)
            return BackupVolumeStatus.Ambiguous;
        if (active is { Status: BackupJobStatus.Running or BackupJobStatus.Dispatched })
            return BackupVolumeStatus.Running;
        if (!nodeOnline)
            return BackupVolumeStatus.NodeOffline;
        if (active is not null)
            return BackupVolumeStatus.Queued;
        if (last is { Status: BackupJobStatus.Failed } && (lastGood is null || last.QueuedAt > lastGood))
            return BackupVolumeStatus.Failed;
        if (lastGood is null)
            return BackupVolumeStatus.Never;
        return now - lastGood > TimeSpan.FromHours(settings.StaleAfterHours) ? BackupVolumeStatus.Stale : BackupVolumeStatus.Ok;
    }

    public static string? ReplaceBlockedReason(
        CatalogVolume volume, SwarmReport? swarm, IReadOnlySet<string> readyNodes, IReadOnlyDictionary<string, NodeRole> roles)
    {
        if (volume.Kind != CatalogVolumeKind.Single || volume.NodeName is null)
            return "Only a volume with one live copy can be replaced";
        if (!readyNodes.Contains(volume.NodeName))
            return $"{volume.NodeName} is offline or not approved";

        var services = swarm?.Services.Where(s => s.Volumes.Contains(volume.VolumeName)).ToList() ?? [];
        if (services.Count == 0)
        {
            // Without a swarm view a service's task can't be stopped: Swarm would start a new one straight away.
            return swarm is null && roles.GetValueOrDefault(volume.NodeName) != NodeRole.Standalone
                ? "No manager agent is reporting the swarm"
                : null;
        }

        if (!readyNodes.Any(node => swarm!.Nodes.Any(n => n.Hostname == node && n.Role == "manager")))
            return "No approved manager agent is online";

        foreach (var service in services)
        {
            if (service.Global)
                return $"{service.Name} is a global service";
            var eligible = PlacementEvaluator.EligibleNodes(service.Constraints, swarm!.Nodes);
            if (eligible is null)
                return $"Can't tell where {service.Name} may run";
            if (eligible.Count != 1 || eligible[0] != volume.NodeName)
                return $"{service.Name} can run on {eligible.Count} nodes; pin it to {volume.NodeName}";
        }

        return null;
    }
}
