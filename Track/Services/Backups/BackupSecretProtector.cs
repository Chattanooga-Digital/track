using Microsoft.AspNetCore.DataProtection;

namespace Track.Services.Backups;

public sealed class BackupSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Track.BackupSecrets.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
