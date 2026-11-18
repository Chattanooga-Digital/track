using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Track.Enums;
using Track.Website.Api.v0.Models.Backups;

namespace Track.Website.Services.Backups;

/// <summary>Central: checks the agent key can write, versioning and object lock are on, and the key can't delete a version.</summary>
public sealed class BackupTargetTester(ResticRunner restic)
{
    private readonly ResticRunner _restic = restic;

    public async Task<List<BackupTargetCheckModel>> TestAsync(
        string endpoint, string region, string bucket, string prefix, S3Addressing addressing, string accessKey, string secretKey,
        CancellationToken cancellationToken)
    {
        var checks = new List<BackupTargetCheckModel>();
        try
        {
            checks.Add(new BackupTargetCheckModel { Label = "restic", Ok = true, Detail = await _restic.VersionAsync(cancellationToken) });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            checks.Add(new BackupTargetCheckModel { Label = "restic", Ok = false, Detail = ex.Message });
        }

        using var client = CreateClient(endpoint, region, addressing, accessKey, secretKey);
        var trimmed = prefix.Trim('/');
        var key = $"{(trimmed.Length > 0 ? trimmed + "/" : "")}.track-test/{Guid.NewGuid():N}";

        string? versionId;
        try
        {
            var put = await client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, ContentBody = "track" }, cancellationToken);
            versionId = put.VersionId;
            checks.Add(new BackupTargetCheckModel { Label = "Agent key can write", Ok = true });
        }
        catch (AmazonServiceException ex)
        {
            checks.Add(new BackupTargetCheckModel { Label = "Agent key can write", Ok = false, Detail = ex.Message });
            return checks;
        }

        try
        {
            var versioning = await client.GetBucketVersioningAsync(new GetBucketVersioningRequest { BucketName = bucket }, cancellationToken);
            var enabled = versioning.VersioningConfig?.Status == VersionStatus.Enabled;
            checks.Add(new BackupTargetCheckModel { Label = "Versioning on", Ok = enabled, Detail = enabled ? null : "Turn on versioning for the bucket" });
        }
        catch (AmazonServiceException ex)
        {
            checks.Add(new BackupTargetCheckModel { Label = "Versioning on", Ok = false, Detail = ex.Message });
        }

        try
        {
            var lockConfig = await client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest { BucketName = bucket }, cancellationToken);
            var retention = lockConfig.ObjectLockConfiguration?.Rule?.DefaultRetention;
            var days = retention?.Days ?? (retention?.Years * 365);
            var on = lockConfig.ObjectLockConfiguration?.ObjectLockEnabled == ObjectLockEnabled.Enabled && days > 0;
            checks.Add(new BackupTargetCheckModel
            {
                Label = "Object lock",
                Ok = on,
                Detail = on ? $"{days} days, {retention!.Mode?.Value?.ToLowerInvariant()}" : "No default retention on the bucket",
            });
        }
        catch (AmazonServiceException ex)
        {
            checks.Add(new BackupTargetCheckModel { Label = "Object lock", Ok = false, Detail = ex.ErrorCode ?? ex.Message });
        }

        if (!string.IsNullOrEmpty(versionId) && versionId != "null")
        {
            try
            {
                await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key, VersionId = versionId }, cancellationToken);
                checks.Add(new BackupTargetCheckModel
                {
                    Label = "Agent key can't delete stored versions",
                    Ok = false,
                    Detail = "It deleted the test object's version, so a compromised node could erase backups",
                });
            }
            catch (AmazonServiceException)
            {
                checks.Add(new BackupTargetCheckModel { Label = "Agent key can't delete stored versions", Ok = true });
            }
        }

        return checks;
    }

    public static AmazonS3Client CreateClient(string endpoint, string region, S3Addressing addressing, string accessKey, string secretKey)
    {
        var host = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : endpoint;
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            AuthenticationRegion = region,
            ForcePathStyle = addressing switch
            {
                S3Addressing.Path => true,
                S3Addressing.VirtualHost => false,
                _ => !host.EndsWith("amazonaws.com", StringComparison.OrdinalIgnoreCase),
            },
            // AWSSDK v4's flexible checksums are rejected by several S3-compatible stores; send them only when required.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        return new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
    }
}
