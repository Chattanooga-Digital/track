namespace Track.Website.Api.v0.Models.Buckets;

/// <summary>Identifies a single bucket for assignment: its source plus bucket name.</summary>
public class BucketRefModel
{
    public required string SourceName { get; set; }
    public required string BucketName { get; set; }
}
