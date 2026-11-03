using Track.Data;
using Track.Data.Models;
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

    /// <summary>All known stacks: currently running, recently seen, assigned to a customer, or shared.</summary>
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

        var sharedNames = (await TrackContext.SharedStacks
                .AsNoTracking()
                .Select(s => s.StackName)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stacks = activeCounts.Keys
            .Union(recentNames, StringComparer.OrdinalIgnoreCase)
            .Union(assignments.Select(a => a.StackName), StringComparer.OrdinalIgnoreCase)
            .Union(sharedNames, StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .Select(name =>
            {
                var assignment = assignmentByStack.GetValueOrDefault(name);
                return new StackModel
                {
                    StackName = name,
                    CustomerId = assignment?.CustomerId,
                    CustomerName = assignment?.Customer?.Name,
                    IsShared = sharedNames.Contains(name),
                    ContainerCount = activeCounts.GetValueOrDefault(name),
                    IsActive = activeCounts.ContainsKey(name),
                };
            })
            .ToList();

        return Ok(stacks);
    }

    /// <summary>Replaces the shared infrastructure stacks; stacks assigned to a customer are moved here.</summary>
    [HttpPut("shared")]
    public async Task<IActionResult> SetShared([FromBody] List<string> stackNames, CancellationToken cancellationToken)
    {
        var names = stackNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = await TrackContext.SharedStacks.ToListAsync(cancellationToken);
        TrackContext.SharedStacks.RemoveRange(existing
            .Where(s => !names.Contains(s.StackName, StringComparer.OrdinalIgnoreCase)));
        TrackContext.SharedStacks.AddRange(names
            .Where(n => !existing.Any(s => string.Equals(s.StackName, n, StringComparison.OrdinalIgnoreCase)))
            .Select(n => new SharedStack { StackName = n }));

        TrackContext.StackAssignments.RemoveRange(await TrackContext.StackAssignments
            .Where(a => names.Contains(a.StackName))
            .ToListAsync(cancellationToken));

        await TrackContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }
}
