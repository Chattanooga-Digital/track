using Track.Services.S3;
using Microsoft.AspNetCore.DataProtection;

namespace Track.Tests;

public class S3SourceSecretProtectorTests
{
    private static S3SourceSecretProtector NewProtector() =>
        new(DataProtectionProvider.Create("Track.Tests"));

    [Test]
    public void ProtectThenUnprotect_RoundTripsTheSecret()
    {
        var protector = NewProtector();
        const string secret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

        var protectedSecret = protector.Protect(secret);

        Assert.That(protectedSecret, Is.Not.EqualTo(secret));
        Assert.That(protector.Unprotect(protectedSecret), Is.EqualTo(secret));
    }
}
