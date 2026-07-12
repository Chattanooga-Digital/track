using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Services.S3;
using Track.Website.Api.v0.Models.S3;
using Track.Website.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

/// <summary>
/// Manages the S3 sources the central polls for bucket sizes. Secret keys are stored encrypted and never
/// returned. Saving a source polls it right away so its buckets are assignable without waiting for the slow
/// poll interval; disabling or deleting one drops its live buckets immediately.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/v0/s3-sources")]
public class S3SourcesController(
    ILogger<S3SourcesController> logger,
    TrackContext metricsContext,
    S3SourceSecretProtector secretProtector,
    S3UsageCollector collector,
    CurrentStateHolder currentStateHolder,
    S3UsageRefresher refresher) : ControllerBase
{
    protected ILogger<S3SourcesController> Logger { get; } = logger;
    protected TrackContext TrackContext { get; } = metricsContext;
    protected S3SourceSecretProtector SecretProtector { get; } = secretProtector;
    protected S3UsageCollector Collector { get; } = collector;
    protected CurrentStateHolder CurrentStateHolder { get; } = currentStateHolder;
    protected S3UsageRefresher Refresher { get; } = refresher;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var sources = await TrackContext.S3Sources
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new S3SourceModel
            {
                Id = s.Id,
                Name = s.Name,
                Endpoint = s.Endpoint,
                Kind = s.Kind,
                AccessKey = s.AccessKey,
                Region = s.Region,
                Enabled = s.Enabled,
                HasSecret = s.SecretKeyProtected != "",
                CreatedAt = s.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return Ok(sources);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] S3SourceEditModel model, CancellationToken cancellationToken)
    {
        var name = model.Name.Trim();
        if (name.Length == 0)
            return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(model.Endpoint))
            return BadRequest("Endpoint is required.");
        if (string.IsNullOrEmpty(model.SecretKey))
            return BadRequest("Secret key is required.");

        if (await TrackContext.S3Sources.AnyAsync(s => s.Name == name, cancellationToken))
            return Conflict("A source with that name already exists.");

        var source = new S3Source
        {
            Name = name,
            Endpoint = model.Endpoint.Trim(),
            Kind = model.Kind,
            AccessKey = model.AccessKey.Trim(),
            SecretKeyProtected = SecretProtector.Protect(model.SecretKey),
            Region = string.IsNullOrWhiteSpace(model.Region) ? "us-east-1" : model.Region.Trim(),
            Enabled = model.Enabled,
            CreatedAt = DateTime.UtcNow,
        };
        TrackContext.S3Sources.Add(source);
        await TrackContext.SaveChangesAsync(cancellationToken);

        // Poll it now: otherwise its buckets stay invisible (and unassignable) until the next slow cycle.
        if (source.Enabled)
            await Refresher.RefreshAsync(source.Name, cancellationToken);

        return Ok(new S3SourceModel
        {
            Id = source.Id,
            Name = source.Name,
            Endpoint = source.Endpoint,
            Kind = source.Kind,
            AccessKey = source.AccessKey,
            Region = source.Region,
            Enabled = source.Enabled,
            HasSecret = true,
            CreatedAt = source.CreatedAt,
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        [FromRoute] int id, [FromBody] S3SourceEditModel model, CancellationToken cancellationToken)
    {
        var source = await TrackContext.S3Sources.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(model.Endpoint))
            return BadRequest("Endpoint is required.");

        // Name is the stable key that bucket assignments reference, so it is not editable here.
        source.Endpoint = model.Endpoint.Trim();
        source.Kind = model.Kind;
        source.AccessKey = model.AccessKey.Trim();
        source.Region = string.IsNullOrWhiteSpace(model.Region) ? "us-east-1" : model.Region.Trim();
        source.Enabled = model.Enabled;
        if (!string.IsNullOrEmpty(model.SecretKey))
            source.SecretKeyProtected = SecretProtector.Protect(model.SecretKey);

        await TrackContext.SaveChangesAsync(cancellationToken);

        if (source.Enabled)
        {
            // Re-poll with the new settings so corrected credentials or endpoints take effect at once.
            await Refresher.RefreshAsync(source.Name, cancellationToken);
        }
        else
        {
            // A disabled source stops contributing immediately.
            CurrentStateHolder.RemoveBucketSource(source.Name);
            await Refresher.BroadcastAsync(cancellationToken);
        }

        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        var source = await TrackContext.S3Sources.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null)
            return NotFound();

        // Drop the source's bucket-to-customer assignments; they are meaningless without the source.
        var assignments = await TrackContext.BucketAssignments
            .Where(a => a.SourceName == source.Name)
            .ToListAsync(cancellationToken);
        TrackContext.BucketAssignments.RemoveRange(assignments);
        TrackContext.S3Sources.Remove(source);
        await TrackContext.SaveChangesAsync(cancellationToken);

        CurrentStateHolder.RemoveBucketSource(source.Name);
        await Refresher.BroadcastAsync(cancellationToken);

        return Ok();
    }

    /// <summary>Polls every enabled source now, rather than waiting for the next slow cycle.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var bucketCount = await Refresher.RefreshAsync(cancellationToken: cancellationToken);
        return Ok(new S3SourceRefreshResultModel { BucketCount = bucketCount });
    }

    /// <summary>Tries to reach the source with the supplied settings and reports how many buckets it found.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test(
        [FromQuery] int? id, [FromBody] S3SourceEditModel model, CancellationToken cancellationToken)
    {
        // Fall back to the stored secret when the form left it blank (editing without re-entering it).
        var secret = model.SecretKey;
        if (string.IsNullOrEmpty(secret) && id is not null)
        {
            var stored = await TrackContext.S3Sources
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (stored is not null)
                secret = SecretProtector.Unprotect(stored.SecretKeyProtected);
        }

        if (string.IsNullOrEmpty(secret))
            return Ok(new S3SourceTestResultModel { Ok = false, Error = "Secret key is required." });

        var target = new S3PollTarget(
            model.Name.Trim(), model.Endpoint.Trim(), model.Kind, model.AccessKey.Trim(), secret,
            string.IsNullOrWhiteSpace(model.Region) ? "us-east-1" : model.Region.Trim());

        try
        {
            var readings = await Collector.CollectAsync(target, cancellationToken);
            return Ok(new S3SourceTestResultModel { Ok = true, BucketCount = readings.Count });
        }
        catch (Exception ex)
        {
            return Ok(new S3SourceTestResultModel { Ok = false, Error = ex.Message });
        }
    }
}
