using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Website.Api.v0.Models.Buckets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

[ApiController]
[Authorize]
[Route("/api/v0/buckets")]
public class BucketsController(
    ILogger<BucketsController> logger,
    TrackContext metricsContext,
    CurrentStateHolder currentStateHolder
) : ControllerBase
{
    protected ILogger<BucketsController> Logger { get; } = logger;
    protected TrackContext TrackContext { get; } = metricsContext;
    protected CurrentStateHolder CurrentStateHolder { get; } = currentStateHolder;

    /// <summary>All known buckets: currently polled from an S3 source, assigned to a customer, or shared.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var live = CurrentStateHolder.GetCurrent().Buckets;

        var assignments = await TrackContext.BucketAssignments
            .AsNoTracking()
            .Include(a => a.Customer)
            .ToListAsync(cancellationToken);
        var assignmentByKey = assignments.ToDictionary(
            a => (a.SourceName, a.BucketName), StringTupleComparer);

        var sharedKeys = (await TrackContext.SharedBuckets
                .AsNoTracking()
                .Select(b => new { b.SourceName, b.BucketName })
                .ToListAsync(cancellationToken))
            .Select(b => (b.SourceName, b.BucketName))
            .ToHashSet(StringTupleComparer);

        // Union live buckets with assigned-but-currently-unseen ones, so an assignment never disappears
        // just because a source is temporarily unreachable.
        var keys = live
            .Select(b => (b.SourceName, b.BucketName))
            .Union(assignments.Select(a => (a.SourceName, a.BucketName)), StringTupleComparer)
            .Union(sharedKeys, StringTupleComparer)
            .ToList();

        var usageByKey = live.ToDictionary(b => (b.SourceName, b.BucketName), b => b, StringTupleComparer);

        var buckets = keys
            .OrderBy(k => k.SourceName)
            .ThenBy(k => k.BucketName)
            .Select(key =>
            {
                var assignment = assignmentByKey.GetValueOrDefault(key);
                var usage = usageByKey.GetValueOrDefault(key);
                return new BucketModel
                {
                    SourceName = key.SourceName,
                    BucketName = key.BucketName,
                    SizeBytes = usage?.SizeBytes ?? 0,
                    ObjectCount = usage?.ObjectCount ?? 0,
                    CustomerId = assignment?.CustomerId,
                    CustomerName = assignment?.Customer?.Name,
                    IsShared = sharedKeys.Contains(key),
                    IsActive = usage is not null,
                };
            })
            .ToList();

        return Ok(buckets);
    }

    /// <summary>Moves one bucket to a customer, to shared infrastructure, or out of both.</summary>
    [HttpPut("assignment")]
    public async Task<IActionResult> SetAssignment([FromBody] BucketAssignmentEditModel model, CancellationToken cancellationToken)
    {
        var key = (SourceName: model.SourceName.Trim(), BucketName: model.BucketName.Trim());
        if (key.SourceName.Length == 0 || key.BucketName.Length == 0)
            return BadRequest("Source and bucket names are required.");

        if (model.CustomerId is int customerId
            && !await TrackContext.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
            return NotFound("Customer not found.");

        var assigned = await TrackContext.BucketAssignments
            .Where(a => a.SourceName == key.SourceName)
            .ToListAsync(cancellationToken);
        TrackContext.BucketAssignments.RemoveRange(assigned.Where(a => StringTupleComparer.Equals((a.SourceName, a.BucketName), key)));
        var shared = await TrackContext.SharedBuckets
            .Where(b => b.SourceName == key.SourceName)
            .ToListAsync(cancellationToken);
        TrackContext.SharedBuckets.RemoveRange(shared.Where(b => StringTupleComparer.Equals((b.SourceName, b.BucketName), key)));

        if (model.CustomerId is int id)
            TrackContext.BucketAssignments.Add(new BucketAssignment { SourceName = key.SourceName, BucketName = key.BucketName, CustomerId = id });
        else if (model.IsShared)
            TrackContext.SharedBuckets.Add(new SharedBucket { SourceName = key.SourceName, BucketName = key.BucketName });

        await TrackContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }

    /// <summary>Replaces the shared infrastructure buckets; buckets assigned to a customer are moved here.</summary>
    [HttpPut("shared")]
    public async Task<IActionResult> SetShared([FromBody] List<BucketRefModel> buckets, CancellationToken cancellationToken)
    {
        var refs = buckets
            .Select(b => (SourceName: b.SourceName.Trim(), BucketName: b.BucketName.Trim()))
            .Where(b => b.SourceName.Length > 0 && b.BucketName.Length > 0)
            .Distinct(StringTupleComparer)
            .ToHashSet(StringTupleComparer);

        var existing = await TrackContext.SharedBuckets.ToListAsync(cancellationToken);
        TrackContext.SharedBuckets.RemoveRange(existing.Where(b => !refs.Contains((b.SourceName, b.BucketName))));
        var existingKeys = existing.Select(b => (b.SourceName, b.BucketName)).ToHashSet(StringTupleComparer);
        TrackContext.SharedBuckets.AddRange(refs
            .Where(r => !existingKeys.Contains(r))
            .Select(r => new SharedBucket { SourceName = r.SourceName, BucketName = r.BucketName }));

        // EF Core can't translate a tuple Contains, so match on the source set and filter names in memory.
        var sources = refs.Select(r => r.SourceName).Distinct().ToList();
        var assigned = await TrackContext.BucketAssignments
            .Where(a => sources.Contains(a.SourceName))
            .ToListAsync(cancellationToken);
        TrackContext.BucketAssignments.RemoveRange(assigned.Where(a => refs.Contains((a.SourceName, a.BucketName))));

        await TrackContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }

    private static readonly TupleComparer StringTupleComparer = new();

    /// <summary>Compares (source, bucket) identities case-insensitively, matching the snapshot's grouping.</summary>
    private sealed class TupleComparer : IEqualityComparer<(string SourceName, string BucketName)>
    {
        public bool Equals((string SourceName, string BucketName) x, (string SourceName, string BucketName) y) =>
            string.Equals(x.SourceName, y.SourceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.BucketName, y.BucketName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string SourceName, string BucketName) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourceName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.BucketName));
    }
}
