using Track.Services;
using Track.Website.Api.v0.Models.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Track.Website.Api.v0.Controllers;

/// <summary>
/// Lists label-less standalone containers that have not yet resolved to a group, with their env var key names,
/// so the operator can choose an identity env var. Once a container resolves to a stack name it drops off this
/// list and appears as a normal stack, assignable through the existing customer UI.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/v0/inventory")]
public class InventoryController(
    ContainerInventoryHolder inventoryHolder,
    CurrentStateHolder currentStateHolder
) : ControllerBase
{
    protected ContainerInventoryHolder InventoryHolder { get; } = inventoryHolder;
    protected CurrentStateHolder CurrentStateHolder { get; } = currentStateHolder;

    [HttpGet]
    public IActionResult Get()
    {
        // Containers whose live sample already carries a stack name are "identified"; exclude them.
        var resolvedIds = CurrentStateHolder.GetCurrent().Samples
            .Where(s => s.StackName is not null)
            .Select(s => s.ContainerId)
            .ToHashSet();

        var containers = InventoryHolder.GetAll()
            .Where(item => !resolvedIds.Contains(item.Container.ContainerId))
            .Select(item => new UnidentifiedContainerModel
            {
                NodeName = item.NodeName,
                ContainerId = item.Container.ContainerId,
                ContainerName = item.Container.ContainerName,
                Image = item.Container.Image,
                EnvKeys = item.Container.EnvKeys,
            })
            .OrderBy(m => m.NodeName)
            .ThenBy(m => m.ContainerName)
            .ToList();

        return Ok(containers);
    }
}
