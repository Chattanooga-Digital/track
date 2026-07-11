using Track.Data;
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

    /// <summary>All known buckets: currently polled from an S3 source, or assigned to a customer.</summary>
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

        // Union live buckets with assigned-but-currently-unseen ones, so an assignment never disappears
        // just because a source is temporarily unreachable.
        var keys = live
            .Select(b => (b.SourceName, b.BucketName))
            .Union(assignments.Select(a => (a.SourceName, a.BucketName)), StringTupleComparer)
            .ToList();

        var sizeByKey = live.ToDictionary(b => (b.SourceName, b.BucketName), b => b.SizeBytes, StringTupleComparer);
        var liveKeys = new HashSet<(string, string)>(
            live.Select(b => (b.SourceName, b.BucketName)), StringTupleComparer);

        var buckets = keys
            .OrderBy(k => k.SourceName)
            .ThenBy(k => k.BucketName)
            .Select(key =>
            {
                var assignment = assignmentByKey.GetValueOrDefault(key);
                return new BucketModel
                {
                    SourceName = key.SourceName,
                    BucketName = key.BucketName,
                    SizeBytes = sizeByKey.GetValueOrDefault(key),
                    CustomerId = assignment?.CustomerId,
                    CustomerName = assignment?.Customer?.Name,
                    IsActive = liveKeys.Contains(key),
                };
            })
            .ToList();

        return Ok(buckets);
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
