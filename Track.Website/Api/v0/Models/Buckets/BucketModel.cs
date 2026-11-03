namespace Track.Website.Api.v0.Models.Buckets;

/// <summary>One S3 bucket: its identity, latest size, and the customer it is assigned to (if any).</summary>
public class BucketModel
{
    public required string SourceName { get; set; }
    public required string BucketName { get; set; }
    public long SizeBytes { get; set; }
    public long ObjectCount { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public bool IsShared { get; set; }

    /// <summary>True when the bucket was seen in the most recent S3 poll (not only known from an assignment).</summary>
    public bool IsActive { get; set; }
}
