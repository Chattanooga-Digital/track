using Track.Data;
using Track.Services;
using Track.Website.Api.v0.Models.Stacks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

[ApiController]
[Authorize]
[Route("/api/v0/stacks")]
public class StacksController(
    ILogger<StacksController> logger,
    TrackContext metricsContext,
    CurrentStateHolder currentStateHolder
) : ControllerBase
{
    protected ILogger<StacksController> Logger { get; } = logger;
    protected TrackContext TrackContext { get; } = metricsContext;
    protected CurrentStateHolder CurrentStateHolder { get; } = currentStateHolder;

    /// <summary>All known stacks: currently running, recently seen, or assigned to a customer.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var state = CurrentStateHolder.GetCurrent();
        var activeCounts = state.Samples
            .Where(s => s.StackName is not null)
            .GroupBy(s => s.StackName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var recentCutoff = DateTime.UtcNow.AddHours(-24);
        var recentNames = await TrackContext.ContainerSamples
            .Where(s => s.Timestamp >= recentCutoff && s.StackName != null)
            .Select(s => s.StackName!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var assignments = await TrackContext.StackAssignments
            .AsNoTracking()
            .Include(a => a.Customer)
            .ToListAsync(cancellationToken);
        var assignmentByStack = assignments.ToDictionary(a => a.StackName, StringComparer.OrdinalIgnoreCase);

        var stacks = activeCounts.Keys
            .Union(recentNames, StringComparer.OrdinalIgnoreCase)
            .Union(assignments.Select(a => a.StackName), StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .Select(name =>
            {
                var assignment = assignmentByStack.GetValueOrDefault(name);
                return new StackModel
                {
                    StackName = name,
                    CustomerId = assignment?.CustomerId,
                    CustomerName = assignment?.Customer?.Name,
                    ContainerCount = activeCounts.GetValueOrDefault(name),
                    IsActive = activeCounts.ContainsKey(name),
                };
            })
            .ToList();

        return Ok(stacks);
    }
}
