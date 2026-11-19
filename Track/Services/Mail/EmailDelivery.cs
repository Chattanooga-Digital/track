using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Track.Data;
using Track.Data.Models;
using Track.Enums;

namespace Track.Services.Mail;

public sealed class EmailDelivery(
    ILogger<EmailDelivery> logger,
    TrackContext context,
    MailSecretProtector protector,
    IMailTransport transport)
{
    public const int RetentionDays = 30;
    private const int BatchSize = 20;
    private const int ErrorLength = 1000;

    private readonly ILogger<EmailDelivery> _logger = logger;
    private readonly TrackContext _context = context;
    private readonly MailSecretProtector _protector = protector;
    private readonly IMailTransport _transport = transport;

    public async Task DeliverDueAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await _context.OutboundEmails
            .Where(e => e.CreatedAt < now.AddDays(-RetentionDays))
            .ExecuteDeleteAsync(cancellationToken);

        var stored = await _context.MailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (stored is not { Enabled: true })
            return;
        var settings = SmtpSettings.From(stored, _protector);

        var due = await _context.OutboundEmails
            .Where(e => e.Status == EmailStatus.Queued && e.NextAttemptAt <= now)
            .OrderBy(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var email in due)
        {
            var failure = await TrySendAsync(settings, email, cancellationToken);
            if (failure is null)
            {
                email.Status = EmailStatus.Sent;
                email.SentAt = DateTime.UtcNow;
                email.LastError = null;
            }
            else
            {
                email.Attempts++;
                email.LastError = failure.Message.Length > ErrorLength ? failure.Message[..ErrorLength] : failure.Message;
                if (RetryDelay(email.Attempts) is { } delay)
                    email.NextAttemptAt = DateTime.UtcNow + delay;
                else
                    email.Status = EmailStatus.Failed;
            }
            await _context.SaveChangesAsync(cancellationToken);

            if (failure is not null)
            {
                _logger.LogWarning(failure, "Email {EmailId} to {Address} failed on attempt {Attempt}",
                    email.Id, email.ToAddress, email.Attempts);

                // The rest would most likely wait out the same timeout against the same server.
                break;
            }
        }
    }

    private async Task<Exception?> TrySendAsync(SmtpSettings settings, OutboundEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await _transport.SendAsync(
                settings, email.ToAddress, email.ToName, new RenderedEmail(email.Subject, email.HtmlBody), cancellationToken);
            return null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return ex;
        }
    }

    public static TimeSpan? RetryDelay(int failedAttempts) => failedAttempts switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(30),
        4 => TimeSpan.FromHours(2),
        _ => null,
    };
}
