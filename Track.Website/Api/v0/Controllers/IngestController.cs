using System.Security.Cryptography;
using System.Text;
using Track.Configuration;
using Track.Data;
using Track.Services;
using Track.Services.Ingest;
using Track.Website.Hubs;
using Track.Website.Queries;
using Track.Website.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Track.Website.Api.v0.Controllers;

/// <summary>
/// Receives metric pushes from agents (one per swarm node) and merges them into the central state exactly
/// as a local poll would. Authenticated by a shared secret header rather than the login cookie, since the
/// caller is a service, not a browser session.
/// </summary>
[ApiController]
[Route("/api/v0/ingest")]
public class IngestController(
    IOptions<TrackOptions> options,
    CurrentStateHolder currentStateHolder,
    ContainerInventoryHolder inventoryHolder,
    IdentityEnvVarStore identityEnvVarStore,
    TrackContext metricsContext,
    SnapshotBuilder snapshotBuilder,
    IHubContext<TrackHub> hubContext) : ControllerBase
{
    protected TrackOptions Options { get; } = options.Value;
    protected CurrentStateHolder CurrentStateHolder { get; } = currentStateHolder;
    protected ContainerInventoryHolder InventoryHolder { get; } = inventoryHolder;
    protected IdentityEnvVarStore IdentityEnvVarStore { get; } = identityEnvVarStore;
    protected TrackContext TrackContext { get; } = metricsContext;
    protected SnapshotBuilder SnapshotBuilder { get; } = snapshotBuilder;
    protected IHubContext<TrackHub> HubContext { get; } = hubContext;

    [HttpPost("stats")]
    public async Task<IActionResult> PostStats([FromBody] StatsReport report, CancellationToken cancellationToken)
    {
        if (!Authorized())
            return Unauthorized();

        CurrentStateHolder.SetNodeSamples(
            report.NodeName, new NodeCollection(report.Capacity, report.Samples, report.Inventory), report.Timestamp);
        InventoryHolder.SetNodeInventory(report.NodeName, report.Inventory);

        TrackContext.ContainerSamples.AddRange(report.Samples);
        await TrackContext.SaveChangesAsync(cancellationToken);

        await BroadcastAsync(cancellationToken);

        // Hand the agent the authoritative identity env-var list so grouping is driven from the central UI.
        return Ok(new IngestResponse([.. IdentityEnvVarStore.Current], AppVersion.Build));
    }

    [HttpPost("disk")]
    public async Task<IActionResult> PostDisk([FromBody] DiskReport report, CancellationToken cancellationToken)
    {
        if (!Authorized())
            return Unauthorized();

        CurrentStateHolder.SetNodeDisk(report.NodeName, report.Samples);

        TrackContext.DiskSamples.AddRange(report.Samples);
        await TrackContext.SaveChangesAsync(cancellationToken);

        await BroadcastAsync(cancellationToken);
        return Ok();
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        var snapshot = await SnapshotBuilder.BuildAsync(cancellationToken);
        await HubContext.Clients.All.SendAsync(TrackHub.SnapshotEvent, snapshot, cancellationToken);
    }

    /// <summary>
    /// Constant-time comparison of the request's shared secret against the configured ingest key. Ingest is
    /// disabled (all requests rejected) until Track:Ingest:Key is set.
    /// </summary>
    private bool Authorized()
    {
        var expected = Options.Ingest.Key;
        if (string.IsNullOrEmpty(expected))
            return false;

        if (!Request.Headers.TryGetValue(CentralClient.KeyHeader, out var provided) || provided.Count != 1)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided[0]!), Encoding.UTF8.GetBytes(expected));
    }
}
