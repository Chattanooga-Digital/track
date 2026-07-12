namespace Track.Website.Api.v0.Models.S3;

/// <summary>Outcome of an on-demand poll of every enabled S3 source.</summary>
public class S3SourceRefreshResultModel
{
    /// <summary>How many buckets were read across all enabled sources.</summary>
    public int BucketCount { get; set; }
}
