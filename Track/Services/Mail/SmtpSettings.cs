using Track.Data.Models;
using Track.Enums;

namespace Track.Services.Mail;

public sealed record SmtpSettings(
    string Host,
    int Port,
    MailSecurity Security,
    string? UserName,
    string? Password,
    string FromName,
    string FromAddress)
{
    public static SmtpSettings From(MailSettings settings, MailSecretProtector protector) => new(
        settings.Host,
        settings.Port,
        settings.Security,
        settings.UserName,
        settings.PasswordProtected is { Length: > 0 } password ? protector.Unprotect(password) : null,
        settings.FromName,
        settings.FromAddress);
}
