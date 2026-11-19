namespace Track.Services.Mail;

public interface IMailTransport
{
    Task SendAsync(SmtpSettings settings, string toAddress, string? toName, RenderedEmail email, CancellationToken cancellationToken);
}
