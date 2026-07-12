using Microsoft.AspNetCore.DataProtection;

namespace Track.Services.S3;

/// <summary>
/// Encrypts and decrypts S3 source secret keys with Data Protection, so credentials are never stored or
/// returned in plaintext. Uses the same key ring as the auth cookie, so set Track:DataProtectionKeysPath
/// to a mounted volume or stored secrets become unreadable after a restart.
/// </summary>
public sealed class S3SourceSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Track.S3SourceSecrets.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
