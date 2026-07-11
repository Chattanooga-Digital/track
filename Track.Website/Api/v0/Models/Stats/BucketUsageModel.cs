namespace Track.Website.Api.v0.Models.Stats;

/// <summary>Latest size of one S3 bucket, shown under the customer it is assigned to.</summary>
public class BucketUsageModel
{
    public required string SourceName { get; set; }
    public required string BucketName { get; set; }
    public long SizeBytes { get; set; }
}
