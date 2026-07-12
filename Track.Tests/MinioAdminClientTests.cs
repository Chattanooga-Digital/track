using Track.Services.S3;

namespace Track.Tests;

public class MinioAdminClientTests
{
    [Test]
    public void BuildSignature_MatchesKnownAnswerVector()
    {
        // Ground truth computed with an independent SigV4 implementation for these fixed inputs.
        var now = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var (amzDate, authorization) = MinioAdminClient.BuildSignature(
            "minio:9000", "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "us-east-1", now);

        Assert.That(amzDate, Is.EqualTo("20260102T030405Z"));
        Assert.That(authorization, Is.EqualTo(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260102/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;x-amz-content-sha256;x-amz-date, "
            + "Signature=53ee4e7ba19c065e6a0a469ffa993194786422f0927400980b7030126f02fa91"));
    }

    [Test]
    public void BuildSignature_ChangesWithTheSecretKey()
    {
        var now = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var (_, a) = MinioAdminClient.BuildSignature("minio:9000", "access", "secret-one", "us-east-1", now);
        var (_, b) = MinioAdminClient.BuildSignature("minio:9000", "access", "secret-two", "us-east-1", now);

        Assert.That(a, Is.Not.EqualTo(b));
    }
}
