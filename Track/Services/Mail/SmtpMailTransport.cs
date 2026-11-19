using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;
using Track.Enums;

namespace Track.Services.Mail;

public sealed class SmtpMailTransport : IMailTransport
{
    private const int TimeoutMilliseconds = 30_000;

    public async Task SendAsync(
        SmtpSettings settings, string toAddress, string? toName, RenderedEmail email, CancellationToken cancellationToken)
    {
        var message = new MimeMessage
        {
            Subject = email.Subject,
            Body = new TextPart(TextFormat.Html) { Text = email.Html },
        };
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(new MailboxAddress(toName ?? "", toAddress));

        using var client = new SmtpClient { Timeout = TimeoutMilliseconds };
        await client.ConnectAsync(settings.Host, settings.Port, SocketOptions(settings.Security), cancellationToken);
        if (!string.IsNullOrEmpty(settings.UserName))
            await client.AuthenticateAsync(settings.UserName, settings.Password ?? "", cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static SecureSocketOptions SocketOptions(MailSecurity security) => security switch
    {
        MailSecurity.StartTls => SecureSocketOptions.StartTls,
        MailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        MailSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto,
    };
}
