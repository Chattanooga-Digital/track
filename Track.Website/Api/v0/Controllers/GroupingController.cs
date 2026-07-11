using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Services.Docker;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

/// <summary>
/// Manages the ordered env-var identity list used to group label-less standalone containers. The list is
/// distributed to agents in the ingest push response, so editing it here changes how agents group containers.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/v0/grouping")]
public class GroupingController(
    TrackContext metricsContext,
    IdentityEnvVarStore identityEnvVarStore
) : ControllerBase
{
    protected TrackContext TrackContext { get; } = metricsContext;
    protected IdentityEnvVarStore IdentityEnvVarStore { get; } = identityEnvVarStore;

    [HttpGet("identity-env-vars")]
    public async Task<IActionResult> GetIdentityEnvVars(CancellationToken cancellationToken)
    {
        var names = await TrackContext.IdentityEnvVars
            .AsNoTracking()
            .OrderBy(e => e.SortOrder)
            .Select(e => e.Name)
            .ToListAsync(cancellationToken);

        return Ok(names);
    }

    /// <summary>Replaces the identity list with the given ordered names (index 0 = highest priority).</summary>
    [HttpPut("identity-env-vars")]
    public async Task<IActionResult> SetIdentityEnvVars(
        [FromBody] List<string> names, CancellationToken cancellationToken)
    {
        // Env keys are case-sensitive; keep order, drop blanks/dupes, respect the column length, and
        // refuse secret-looking names so a secret's value can never be sent to the central as a stack name.
        var ordered = names
            .Select(n => n.Trim())
            .Where(n => n.Length is > 0 and <= 200 && !SensitiveEnvKeys.IsSensitive(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Delete-then-insert in a transaction so the unique index on Name never collides mid-save.
        await using var transaction = await TrackContext.Database.BeginTransactionAsync(cancellationToken);

        TrackContext.IdentityEnvVars.RemoveRange(await TrackContext.IdentityEnvVars.ToListAsync(cancellationToken));
        await TrackContext.SaveChangesAsync(cancellationToken);

        TrackContext.IdentityEnvVars.AddRange(
            ordered.Select((name, index) => new IdentityEnvVar { Name = name, SortOrder = index }));
        await TrackContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        IdentityEnvVarStore.Set(ordered);
        return Ok(ordered);
    }
}
