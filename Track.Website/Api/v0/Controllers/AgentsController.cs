using Track.Data.Models;
using Track.Services;
using Track.Website.Hubs;
using Track.Website.Queries;
using Track.Website.Services.Backups;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Track.Website.Api.v0.Controllers;

/// <summary>
/// Manages the live agent (node) roster. Agents are in-memory state keyed by node hostname, so deleting one
/// forgets a decommissioned agent; a still-running agent simply reappears on its next push, waiting for approval.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/v0/agents")]
public class AgentsController(
    CurrentStateHolder currentStateHolder,
    ContainerInventoryHolder inventoryHolder,
    VolumeInventoryHolder volumeInventoryHolder,
    AgentEnrollmentRegistry enrollment,
    SnapshotBuilder snapshotBuilder,
    IHubContext<TrackHub> hubContext
) : ControllerBase
{
    protected CurrentStateHolder CurrentStateHolder { get; } = currentStateHolder;
    protected ContainerInventoryHolder InventoryHolder { get; } = inventoryHolder;
    protected VolumeInventoryHolder VolumeInventoryHolder { get; } = volumeInventoryHolder;
    protected AgentEnrollmentRegistry Enrollment { get; } = enrollment;
    protected SnapshotBuilder SnapshotBuilder { get; } = snapshotBuilder;
    protected IHubContext<TrackHub> HubContext { get; } = hubContext;

    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete([FromRoute] string name, CancellationToken cancellationToken)
    {
        CurrentStateHolder.RemoveNode(name);
        InventoryHolder.RemoveNodes([name]);
        VolumeInventoryHolder.RemoveNode(name);
        await Enrollment.RemoveAsync(name, cancellationToken);

        await BroadcastAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{name}/approve")]
    [Authorize(Roles = Role.Administrator)]
    public async Task<IActionResult> Approve([FromRoute] string name, CancellationToken cancellationToken)
    {
        if (!await Enrollment.ApproveAsync(name, User.Identity?.Name, cancellationToken))
            return BadRequest("This agent hasn't sent a token yet. Update it to the central's version.");

        await BroadcastAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{name}/revoke")]
    [Authorize(Roles = Role.Administrator)]
    public async Task<IActionResult> Revoke([FromRoute] string name, CancellationToken cancellationToken)
    {
        if (!await Enrollment.RevokeAsync(name, cancellationToken))
            return NotFound();

        VolumeInventoryHolder.RemoveNode(name);
        await BroadcastAsync(cancellationToken);
        return NoContent();
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        // Rebroadcast so every client's agent list updates immediately.
        var snapshot = await SnapshotBuilder.BuildAsync(cancellationToken);
        await HubContext.Clients.All.SendAsync(TrackHub.SnapshotEvent, snapshot, cancellationToken);
    }
}
