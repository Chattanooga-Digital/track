using System.Net;
using System.Text.RegularExpressions;
using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Mail;
using Track.Website.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Track.Tests;

public partial class AccountEmailsTests
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private TrackContext Context => _scope.ServiceProvider.GetRequiredService<TrackContext>();
    private UserManager<User> Users => _scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    private AccountEmails AccountEmails => _scope.ServiceProvider.GetRequiredService<AccountEmails>();

    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
            .AddDbContext<TrackContext>(options => options.UseSqlite(_connection))
            .AddSingleton<EmailRenderer>()
            .AddSingleton<EmailDeliverySignal>()
            .AddScoped<EmailQueue>()
            .AddScoped<AccountEmails>();
        services.AddIdentityCore<User>().AddRoles<Role>().AddEntityFrameworkStores<TrackContext>().AddDefaultTokenProviders();
        _services = services.BuildServiceProvider();
        _scope = _services.CreateAsyncScope();

        await Context.Database.EnsureCreatedAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task ANewUserIsInvitedThenGetsResetsOnceTheyHaveAPassword()
    {
        await EnableMailAsync(enabled: true);
        var user = await CreateUserAsync("ops@example.com");

        Assert.That(await AccountEmails.SendPasswordLinkAsync(user, "Admin", default), Is.True);
        var invitation = await Context.OutboundEmails.AsNoTracking().SingleAsync();
        Assert.That(invitation.Kind, Is.EqualTo(EmailKind.SetPassword));
        Assert.That(invitation.ToAddress, Is.EqualTo("ops@example.com"));
        Assert.That(invitation.HtmlBody, Does.Contain("Admin added you to Track"));

        var (linkUser, token) = Link(invitation.HtmlBody);
        Assert.That(linkUser, Is.EqualTo(user.Id.ToString()));
        Assert.That((await Users.ResetPasswordAsync(user, token, "Chosen-pw1")).Succeeded, Is.True);
        Assert.That(await Users.CheckPasswordAsync(user, "Chosen-pw1"), Is.True);
        Assert.That((await Users.ResetPasswordAsync(user, token, "Again-pw2")).Succeeded, Is.False, "a link works once");

        await AccountEmails.SendPasswordLinkAsync(user, null, default);
        var reset = await Context.OutboundEmails.AsNoTracking().OrderBy(e => e.Id).LastAsync();
        Assert.That(reset.Kind, Is.EqualTo(EmailKind.PasswordReset));
        Assert.That(reset.HtmlBody, Does.Contain("for 24 hours"));
    }

    [Test]
    public async Task NoLinkWithoutAnAddressOrWhileMailIsOff()
    {
        var user = await CreateUserAsync("ops@example.com");
        Assert.That(await AccountEmails.SendPasswordLinkAsync(user, null, default), Is.False);

        await EnableMailAsync(enabled: false);
        Assert.That(await AccountEmails.SendPasswordLinkAsync(user, null, default), Is.False);

        await Context.MailSettings.ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, true));
        var withoutEmail = await CreateUserAsync(null);
        Assert.That(await AccountEmails.SendPasswordLinkAsync(withoutEmail, null, default), Is.False);

        Assert.That(await Context.OutboundEmails.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task RecentLinksAreTrackedPerAddress()
    {
        await EnableMailAsync(enabled: true);
        await AccountEmails.SendPasswordLinkAsync(await CreateUserAsync("ops@example.com"), null, default);

        Assert.That(await AccountEmails.SentRecentlyAsync("ops@example.com", default), Is.True);
        Assert.That(await AccountEmails.SentRecentlyAsync("dev@example.com", default), Is.False);
    }

    private async Task EnableMailAsync(bool enabled)
    {
        Context.MailSettings.Add(new MailSettings
        {
            Enabled = enabled,
            Host = "smtp.example.com",
            FromName = "Track",
            FromAddress = "track@example.com",
            SiteUrl = "https://track.example.com",
        });
        await Context.SaveChangesAsync();
    }

    private async Task<User> CreateUserAsync(string? email)
    {
        var user = new User { UserName = $"user{Guid.NewGuid():N}", Email = email, CreatedAt = DateTime.UtcNow };
        Assert.That((await Users.CreateAsync(user)).Succeeded, Is.True);
        return user;
    }

    private static (string UserId, string Token) Link(string html)
    {
        var match = LinkPattern().Match(WebUtility.HtmlDecode(html));
        Assert.That(match.Success, Is.True, "the email carries a set-password link");
        return (match.Groups[1].Value, Uri.UnescapeDataString(match.Groups[2].Value));
    }

    [GeneratedRegex(@"https://track\.example\.com/set-password\?user=([0-9a-f-]+)&token=([^""<\s]+)")]
    private static partial Regex LinkPattern();
}
