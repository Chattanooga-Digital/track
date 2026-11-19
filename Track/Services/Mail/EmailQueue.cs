using Microsoft.EntityFrameworkCore;
using Track.Data;
using Track.Data.Models;
using Track.Enums;

namespace Track.Services.Mail;

/// <summary>Renders an email now and leaves sending and retrying to <see cref="EmailDelivery"/>.</summary>
public sealed class EmailQueue(TrackContext context, EmailRenderer renderer, EmailDeliverySignal signal)
{
    private readonly TrackContext _context = context;
    private readonly EmailRenderer _renderer = renderer;
    private readonly EmailDeliverySignal _signal = signal;

    /// <summary>False, with nothing stored, while mail is off, so turning it on doesn't send a backlog of stale mail.</summary>
    public async Task<bool> QueueAsync(
        EmailKind kind, string toAddress, string? toName, object model, CancellationToken cancellationToken)
    {
        if (!await _context.MailSettings.AnyAsync(s => s.Enabled, cancellationToken))
            return false;

        var email = await _renderer.RenderAsync(kind, model);
        var now = DateTime.UtcNow;
        _context.OutboundEmails.Add(new OutboundEmail
        {
            Kind = kind,
            ToAddress = toAddress,
            ToName = toName,
            Subject = email.Subject,
            HtmlBody = email.Html,
            Status = EmailStatus.Queued,
            CreatedAt = now,
            NextAttemptAt = now,
        });
        await _context.SaveChangesAsync(cancellationToken);
        _signal.Wake();
        return true;
    }
}
