using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Track.Services.S3;

/// <summary>Per-bucket usage from MinIO's admin data-usage API: size in bytes and object count.</summary>
public readonly record struct MinioBucketUsage(string BucketName, long SizeBytes, long ObjectCount);

/// <summary>
/// Reads MinIO/RustFS bucket usage from the admin data-usage API in a single request, avoiding the
/// per-object ListObjectsV2 walk. The endpoint is signed with AWS Signature V4 (service "s3") using the
/// source's access/secret keys - the same scheme the S3 API uses - since there is no official .NET
/// madmin client. Numbers are scanner-derived (the same ones `mc admin info` shows), which is what we want
/// for billing.
/// </summary>
public sealed class MinioAdminClient : IDisposable
{
    private const string DataUsagePath = "/minio/admin/v3/datausageinfo";
    private const string EmptyPayloadSha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConcurrentDictionary<string, HttpClient> _clients = new();

    public async Task<List<MinioBucketUsage>> GetBucketUsageAsync(
        string endpoint, string accessKey, string secretKey, string region, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(endpoint);
        var client = _clients.GetOrAdd(endpoint, _ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, DataUsagePath));
        Sign(request, baseUri, accessKey, secretKey, region, DateTime.UtcNow);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var usage = await JsonSerializer.DeserializeAsync<DataUsageInfo>(stream, JsonOptions, cancellationToken);

        if (usage?.BucketsUsageInfo is not { } buckets)
            return [];

        return buckets
            .Select(b => new MinioBucketUsage(b.Key, b.Value.Size, b.Value.ObjectsCount))
            .ToList();
    }

    /// <summary>Applies an AWS SigV4 authorization header for an empty-body GET to the request.</summary>
    private static void Sign(
        HttpRequestMessage request, Uri baseUri, string accessKey, string secretKey, string region, DateTime now)
    {
        var host = baseUri.IsDefaultPort ? baseUri.Host : $"{baseUri.Host}:{baseUri.Port}";
        var (amzDate, authorization) = BuildSignature(host, accessKey, secretKey, region, now);

        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", EmptyPayloadSha256);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
    }

    /// <summary>
    /// Builds the SigV4 <c>x-amz-date</c> and <c>Authorization</c> header values for an empty-body GET of
    /// the data-usage path. Internal so the signing can be verified against a known-answer test vector.
    /// </summary>
    internal static (string AmzDate, string Authorization) BuildSignature(
        string host, string accessKey, string secretKey, string region, DateTime now)
    {
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var canonicalHeaders =
            $"host:{host}\nx-amz-content-sha256:{EmptyPayloadSha256}\nx-amz-date:{amzDate}\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = string.Join('\n',
            "GET", DataUsagePath, "", canonicalHeaders, signedHeaders, EmptyPayloadSha256);

        var credentialScope = $"{dateStamp}/{region}/s3/aws4_request";
        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256", amzDate, credentialScope, Hex(Sha256(canonicalRequest)));

        var signingKey = SignatureKey(secretKey, dateStamp, region, "s3");
        var signature = Hex(HmacSha256(signingKey, stringToSign));

        var authorization = $"AWS4-HMAC-SHA256 Credential={accessKey}/{credentialScope}, "
            + $"SignedHeaders={signedHeaders}, Signature={signature}";
        return (amzDate, authorization);
    }

    private static byte[] SignatureKey(string secret, string dateStamp, string region, string service)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{secret}"), dateStamp);
        var kRegion = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, service);
        return HmacSha256(kService, "aws4_request");
    }

    private static byte[] Sha256(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static byte[] HmacSha256(byte[] key, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        _clients.Clear();
    }

    private sealed class DataUsageInfo
    {
        public Dictionary<string, BucketUsageInfo>? BucketsUsageInfo { get; set; }
    }

    private sealed class BucketUsageInfo
    {
        public long Size { get; set; }
        public long ObjectsCount { get; set; }
    }
}
