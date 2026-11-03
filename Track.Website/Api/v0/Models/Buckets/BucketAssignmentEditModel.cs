namespace Track.Website.Api.v0.Models.Buckets;

/// <summary>Where one bucket belongs: a customer, shared infrastructure, or neither (unassigned).</summary>
public class BucketAssignmentEditModel
{
    public required string SourceName { get; set; }
    public required string BucketName { get; set; }
    public int? CustomerId { get; set; }

    /// <summary>Ignored when <see cref="CustomerId"/> is set.</summary>
    public bool IsShared { get; set; }
}
