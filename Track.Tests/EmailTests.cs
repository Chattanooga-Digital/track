using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Mail;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Track.Tests;

public class EmailTests
{
    private static readonly EmailRenderer Renderer = new();

    private SqliteConnection _connection = null!;
    private TrackContext _context = null!;
    private MailSecretProtector _protector = null!;

    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _context = new TrackContext(new DbContextOptionsBuilder<TrackContext>().UseSqlite(_connection).Options);
        await _context.Database.EnsureCreatedAsync();
        _protector = new MailSecretProtector(DataProtectionProvider.Create("Track.Tests"));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task EveryEmailKindRendersFromItsTemplate()
    {
        foreach (var kind in Enum.GetValues<EmailKind>())
        {
            var email = await Renderer.RenderAsync(kind, new { });

            Assert.That(email.Subject, Is.Not.Empty, kind.ToString());
            Assert.That(email.Html, Does.Contain("Sent by Track."), kind.ToString());
        }
    }

    [Test]
    public async Task ValuesAreEncodedAndStylesInlined()
    {
        var email = await Renderer.RenderAsync(EmailKind.Test, new { SentBy = "<b>admin</b>", Host = "smtp.example.com" });

        Assert.That(email.Subject, Is.EqualTo("Track test email"));
        Assert.That(email.Html, Does.Contain("&lt;b&gt;admin&lt;/b&gt; sent this test through smtp.example.com."));
        Assert.That(email.Html, Does.Not.Contain("<style"));
        Assert.That(email.Html, Does.Contain("style=\""));
        Assert.That(email.Html, Does.Contain("<title>Track test email</title>"));
    }

    [Test]
    public void ATemplateWithoutASubjectIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => EmailRenderer.SplitFrontMatter("---\nfrom: x\n---\nbody", "Broken"));
        Assert.Throws<InvalidOperationException>(() => EmailRenderer.SplitFrontMatter("body", "Broken"));
    }

    [Test]
    public async Task NothingIsQueuedWhileMailIsOff()
    {
        Assert.That(await NewQueue().QueueAsync(EmailKind.Test, "ops@example.com", null, new { }, default), Is.False);

        await SaveSettingsAsync(enabled: false);
        Assert.That(await NewQueue().QueueAsync(EmailKind.Test, "ops@example.com", null, new { }, default), Is.False);

        Assert.That(await _context.OutboundEmails.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task QueuedEmailIsRenderedAndSentOnTheNextPass()
    {
        await SaveSettingsAsync(enabled: true, password: "hunter2");
        Assert.That(await NewQueue().QueueAsync(EmailKind.Test, "ops@example.com", "Ops", new { SentBy = "admin" }, default), Is.True);

        var transport = new FakeTransport();
        await NewDelivery(transport).DeliverDueAsync(default);

        var email = await _context.OutboundEmails.AsNoTracking().SingleAsync();
        Assert.That(email.Status, Is.EqualTo(EmailStatus.Sent));
        Assert.That(email.SentAt, Is.Not.Null);
        Assert.That(transport.Sent, Has.Count.EqualTo(1));
        var (settings, to, rendered) = transport.Sent[0];
        Assert.That(to, Is.EqualTo("ops@example.com"));
        Assert.That(settings.Password, Is.EqualTo("hunter2"));
        Assert.That(rendered.Html, Does.Contain("admin sent this test"));
    }

    [Test]
    public async Task AFailingEmailIsRetriedThenGivenUp()
    {
        await SaveSettingsAsync(enabled: true);
        await NewQueue().QueueAsync(EmailKind.Test, "ops@example.com", null, new { }, default);
        await NewQueue().QueueAsync(EmailKind.Test, "dev@example.com", null, new { }, default);
        var transport = new FakeTransport { Failure = new IOException("connection refused") };

        await NewDelivery(transport).DeliverDueAsync(default);

        var emails = await _context.OutboundEmails.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.That(transport.Attempts, Is.EqualTo(1), "a failure ends the pass instead of timing out on every email");
        Assert.That(emails[0].Status, Is.EqualTo(EmailStatus.Queued));
        Assert.That(emails[0].Attempts, Is.EqualTo(1));
        Assert.That(emails[0].LastError, Is.EqualTo("connection refused"));
        Assert.That(emails[0].NextAttemptAt, Is.GreaterThan(DateTime.UtcNow));
        Assert.That(emails[1].Attempts, Is.Zero);

        for (var attempt = 2; attempt <= 5; attempt++)
        {
            await _context.OutboundEmails.Where(e => e.Id == emails[0].Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
            await _context.OutboundEmails.Where(e => e.Id == emails[1].Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.NextAttemptAt, DateTime.UtcNow.AddDays(1)));
            await NewDelivery(transport).DeliverDueAsync(default);
        }

        var given = await _context.OutboundEmails.AsNoTracking().SingleAsync(e => e.Id == emails[0].Id);
        Assert.That(given.Attempts, Is.EqualTo(5));
        Assert.That(given.Status, Is.EqualTo(EmailStatus.Failed));
    }

    private EmailQueue NewQueue() => new(_context, Renderer, new EmailDeliverySignal());

    private EmailDelivery NewDelivery(FakeTransport transport)
    {
        _context.ChangeTracker.Clear();
        return new EmailDelivery(NullLogger<EmailDelivery>.Instance, _context, _protector, transport);
    }

    private async Task SaveSettingsAsync(bool enabled, string? password = null)
    {
        _context.MailSettings.Add(new MailSettings
        {
            Enabled = enabled,
            Host = "smtp.example.com",
            UserName = password is null ? null : "track",
            PasswordProtected = password is null ? null : _protector.Protect(password),
            FromName = "Track",
            FromAddress = "track@example.com",
            SiteUrl = "https://track.example.com",
        });
        await _context.SaveChangesAsync();
    }

    private sealed class FakeTransport : IMailTransport
    {
        public Exception? Failure { get; init; }
        public int Attempts { get; private set; }
        public List<(SmtpSettings Settings, string To, RenderedEmail Email)> Sent { get; } = [];

        public Task SendAsync(
            SmtpSettings settings, string toAddress, string? toName, RenderedEmail email, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Failure is not null)
                throw Failure;
            Sent.Add((settings, toAddress, email));
            return Task.CompletedTask;
        }
    }
}
