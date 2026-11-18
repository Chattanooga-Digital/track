using Track.Configuration;
using Track.Data;
using Track.Enums;
using Track.Services;
using Track.Services.Ingest;
using Track.Website.Hubs;
using Track.Website.Queries;
using Track.Website.Services;
using Track.Website.Services.Backups;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Track.Website.Api.v0.Controllers;

/// <summary>
/// Receives metric pushes from agents (one per swarm node) and merges them into the central state exactly
/// as a local poll would. Authenticated by a shared secret header rather than the login cookie, since the
/// caller is a service, not a browser session. Volume and swarm details only count from approved agents.
/// </summary>
[ApiController]
[Route("/api/v0/ingest")]
public class IngestController(
    IOptions<TrackOptions> options,
    CurrentStateHolder currentStateHolder,
    ContainerInventoryHolder inventoryHolder,
    VolumeInventoryHolder volumeInventoryHolder,
    SwarmInventoryHolder swarmInventoryHolder,
    IdentityEnvVarStore identityEnvVarStore,
    AgentEnrollmentRegistry enrollment,
    TrackContext metricsContext,
    SnapshotBuilder snapshotBuilder,
    IHubContext<TrackHub> hubContext) : ControllerBase
{
    protected TrackOptions Options { get; } = options.Value;
    protected CurrentStateHolder CurrentStateHolder { get; } = currentStateHolder;
    protected ContainerInventoryHolder InventoryHolder { get; } = inventoryHolder;
    protected VolumeInventoryHolder VolumeInventoryHolder { get; } = volumeInventoryHolder;
    protected SwarmInventoryHolder SwarmInventoryHolder { get; } = swarmInventoryHolder;
    protected IdentityEnvVarStore IdentityEnvVarStore { get; } = identityEnvVarStore;
    protected AgentEnrollmentRegistry Enrollment { get; } = enrollment;
    protected TrackContext TrackContext { get; } = metricsContext;
    protected SnapshotBuilder SnapshotBuilder { get; } = snapshotBuilder;
    protected IHubContext<TrackHub> HubContext { get; } = hubContext;

    [HttpPost("stats")]
    public async Task<IActionResult> PostStats([FromBody] StatsReport report, CancellationToken cancellationToken)
    {
        if (!IngestKey.IsValid(Request, Options))
            return Unauthorized();

        var approval = await Enrollment.CheckAsync(report.NodeName, IngestKey.AgentToken(Request), cancellationToken);

        CurrentStateHolder.SetNodeSamples(
            report.NodeName,
            new NodeCollection(report.Capacity, report.Samples, report.Inventory, report.Mounts, report.Role),
            report.Timestamp,
            report.AgentVersion);
        InventoryHolder.SetNodeInventory(report.NodeName, report.Inventory);
        if (approval == AgentApproval.Approved)
            VolumeInventoryHolder.SetMounts(report.NodeName, report.Mounts ?? []);

        TrackContext.ContainerSamples.AddRange(report.Samples);
        await TrackContext.SaveChangesAsync(cancellationToken);

        await BroadcastAsync(cancellationToken);

        var jobIds = approval == AgentApproval.Approved
            ? await BackupScheduler.JobIdsForNodeAsync(TrackContext, report.NodeName, cancellationToken)
            : null;

        // Hand the agent the authoritative identity env-var list so grouping is driven from the central UI.
        return Ok(new IngestResponse([.. IdentityEnvVarStore.Current], AppVersion.Build, approval, jobIds));
    }

    [HttpPost("disk")]
    public async Task<IActionResult> PostDisk([FromBody] DiskReport report, CancellationToken cancellationToken)
    {
        if (!IngestKey.IsValid(Request, Options))
            return Unauthorized();

        CurrentStateHolder.SetNodeDisk(report.NodeName, report.Samples);
        if (await Enrollment.CheckAsync(report.NodeName, IngestKey.AgentToken(Request), cancellationToken) == AgentApproval.Approved)
        {
            VolumeInventoryHolder.SetVolumes(report.NodeName, report.Samples
                .Where(s => s.Kind == DiskUsageKind.Volume)
                .Select(s => new NodeVolume(s.Name, s.StackName, s.SizeBytes))
                .ToList());
        }

        TrackContext.DiskSamples.AddRange(report.Samples);
        await TrackContext.SaveChangesAsync(cancellationToken);

        await BroadcastAsync(cancellationToken);
        return Ok();
    }

    [HttpPost("swarm")]
    public async Task<IActionResult> PostSwarm([FromBody] SwarmReport report, CancellationToken cancellationToken)
    {
        if (!IngestKey.IsValid(Request, Options))
            return Unauthorized();

        if (await Enrollment.CheckAsync(report.NodeName, IngestKey.AgentToken(Request), cancellationToken) == AgentApproval.Approved)
            SwarmInventoryHolder.Set(report);

        return Ok();
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        var snapshot = await SnapshotBuilder.BuildAsync(cancellationToken);
        await HubContext.Clients.All.SendAsync(TrackHub.SnapshotEvent, snapshot, cancellationToken);
    }
}
