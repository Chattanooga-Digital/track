using Track.Services;
using Track.Website.Hubs;
using Track.Website.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Track.Website.Api.v0.Controllers;

/// <summary>
/// Manages the live agent (node) roster. Agents are in-memory state keyed by node hostname, so deleting one
/// forgets a decommissioned agent; a still-running agent simply reappears on its next push.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/v0/agents")]
public class AgentsController(
    CurrentStateHolder currentStateHolder,
    ContainerInventoryHolder inventoryHolder,
    SnapshotBuilder snapshotBuilder,
    IHubContext<TrackHub> hubContext
) : ControllerBase
{
    protected CurrentStateHolder CurrentStateHolder { get; } = currentStateHolder;
    protected ContainerInventoryHolder InventoryHolder { get; } = inventoryHolder;
    protected SnapshotBuilder SnapshotBuilder { get; } = snapshotBuilder;
    protected IHubContext<TrackHub> HubContext { get; } = hubContext;

    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete([FromRoute] string name, CancellationToken cancellationToken)
    {
        CurrentStateHolder.RemoveNode(name);
        InventoryHolder.RemoveNodes([name]);

        // Rebroadcast so every client's agent list updates immediately.
        var snapshot = await SnapshotBuilder.BuildAsync(cancellationToken);
        await HubContext.Clients.All.SendAsync(TrackHub.SnapshotEvent, snapshot, cancellationToken);

        return NoContent();
    }
}
