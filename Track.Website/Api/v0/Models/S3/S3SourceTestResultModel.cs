namespace Track.Website.Api.v0.Models.S3;

/// <summary>Outcome of a test connection to an S3 source.</summary>
public class S3SourceTestResultModel
{
    public bool Ok { get; set; }
    public int BucketCount { get; set; }
    public string? Error { get; set; }
}
