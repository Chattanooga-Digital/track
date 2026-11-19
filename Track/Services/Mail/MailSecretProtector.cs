using Microsoft.AspNetCore.DataProtection;

namespace Track.Services.Mail;

public sealed class MailSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Track.MailSecrets.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
