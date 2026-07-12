using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Track.Enums;
using Track.Services;
using Microsoft.Extensions.Logging;

namespace Track.Services.S3;

/// <summary>A source to poll, with its secret already decrypted. Built by the hosted service from the DB.</summary>
public sealed record S3PollTarget(
    string Name, string Endpoint, S3SourceKind Kind, string AccessKey, string SecretKey, string Region);

/// <summary>
/// Sizes the buckets of one S3 source. MinIO/RustFS sources use the admin data-usage API (one request,
/// exact size + object count from the scanner); generic S3 sources fall back to walking ListObjectsV2.
/// Returns per-bucket <see cref="BucketUsage"/> for the central to assign to customers.
/// </summary>
public sealed class S3UsageCollector(MinioAdminClient minioAdminClient, ILogger<S3UsageCollector> logger)
{
    private readonly MinioAdminClient _minioAdminClient = minioAdminClient;
    private readonly ILogger<S3UsageCollector> _logger = logger;

    public async Task<List<BucketUsage>> CollectAsync(S3PollTarget source, CancellationToken cancellationToken = default)
    {
        return source.Kind == S3SourceKind.Minio
            ? await CollectViaAdminAsync(source, cancellationToken)
            : await CollectViaListingAsync(source, cancellationToken);
    }

    private async Task<List<BucketUsage>> CollectViaAdminAsync(S3PollTarget source, CancellationToken cancellationToken)
    {
        var usage = await _minioAdminClient.GetBucketUsageAsync(
            source.Endpoint, source.AccessKey, source.SecretKey, source.Region, cancellationToken);

        return usage
            .Select(b => new BucketUsage(source.Name, b.BucketName, b.SizeBytes, b.ObjectCount))
            .ToList();
    }

    private async Task<List<BucketUsage>> CollectViaListingAsync(S3PollTarget source, CancellationToken cancellationToken)
    {
        using var client = CreateS3Client(source);
        var buckets = await client.ListBucketsAsync(cancellationToken);
        var readings = new List<BucketUsage>();

        foreach (var bucket in buckets.Buckets ?? [])
        {
            // One bad bucket (permissions, deletion mid-poll) must not blank the rest of the source.
            try
            {
                var (size, count) = await SizeBucketAsync(client, bucket.BucketName, cancellationToken);
                readings.Add(new BucketUsage(source.Name, bucket.BucketName, size, count));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to size bucket {Bucket} on S3 source {Source}",
                    bucket.BucketName, source.Name);
            }
        }

        return readings;
    }

    private static async Task<(long Size, long Count)> SizeBucketAsync(
        IAmazonS3 client, string bucketName, CancellationToken cancellationToken)
    {
        long size = 0;
        long count = 0;
        var request = new ListObjectsV2Request { BucketName = bucketName };

        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, cancellationToken);
            foreach (var obj in response.S3Objects ?? [])
            {
                size += obj.Size ?? 0;
                count++;
            }
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        return (size, count);
    }

    private static AmazonS3Client CreateS3Client(S3PollTarget source)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = source.Endpoint,
            // MinIO/RustFS serve buckets on the path, not a per-bucket subdomain.
            ForcePathStyle = true,
            AuthenticationRegion = source.Region,
            // AWSSDK v4 defaults to adding flexible-checksum headers, which MinIO/RustFS fold into
            // signature verification differently than AWS and reject as SignatureDoesNotMatch. Only send
            // checksums when an operation actually requires them.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        return new AmazonS3Client(new BasicAWSCredentials(source.AccessKey, source.SecretKey), config);
    }
}
