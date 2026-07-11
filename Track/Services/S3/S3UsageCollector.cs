using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Track.Configuration;
using Track.Data.Models;
using Track.Enums;
using Microsoft.Extensions.Logging;

namespace Track.Services.S3;

/// <summary>
/// Polls one S3-compatible endpoint (MinIO, AWS, ...) and sizes every bucket by walking its object
/// listing. Each bucket becomes a <see cref="DiskUsageKind.Bucket"/> sample (NodeName = source,
/// Name = bucket) that is assigned to a customer through <see cref="Data.Models.BucketAssignment"/>.
/// </summary>
public sealed class S3UsageCollector(ILogger<S3UsageCollector> logger)
{
    private readonly ILogger<S3UsageCollector> _logger = logger;

    public async Task<List<DiskSample>> CollectAsync(
        S3SourceOptions source, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(source);
        var buckets = await client.ListBucketsAsync(cancellationToken);
        var samples = new List<DiskSample>();

        foreach (var bucket in buckets.Buckets ?? [])
        {
            // One bad bucket (permissions, deletion mid-poll) must not blank the rest of the source.
            try
            {
                samples.Add(new DiskSample
                {
                    Timestamp = timestamp,
                    NodeName = source.Name!,
                    Kind = DiskUsageKind.Bucket,
                    Name = bucket.BucketName,
                    SizeBytes = await SizeBucketAsync(client, bucket.BucketName, cancellationToken),
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to size bucket {Bucket} on S3 source {Source}",
                    bucket.BucketName, source.Name);
            }
        }

        return samples;
    }

    private static async Task<long> SizeBucketAsync(
        IAmazonS3 client, string bucketName, CancellationToken cancellationToken)
    {
        long size = 0;
        var request = new ListObjectsV2Request { BucketName = bucketName };

        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, cancellationToken);
            size += response.S3Objects?.Sum(o => o.Size ?? 0) ?? 0;
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        return size;
    }

    private static AmazonS3Client CreateClient(S3SourceOptions source)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = source.Endpoint,
            // MinIO serves buckets on the path, not a per-bucket subdomain.
            ForcePathStyle = true,
            AuthenticationRegion = source.Region,
        };

        return new AmazonS3Client(new BasicAWSCredentials(source.AccessKey, source.SecretKey), config);
    }
}
