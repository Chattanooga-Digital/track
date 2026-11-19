using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Track.Website.Services;

/// <summary>Emails a user a link to choose their password: an invitation until they have one, a reset after that.</summary>
public sealed class AccountEmails(
    TrackContext context,
    UserManager<User> userManager,
    EmailQueue queue,
    IOptions<DataProtectionTokenProviderOptions> tokenOptions)
{
    public const string SetPasswordPath = "/set-password";
    private static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(5);

    private readonly TrackContext _context = context;
    private readonly UserManager<User> _userManager = userManager;
    private readonly EmailQueue _queue = queue;
    private readonly IOptions<DataProtectionTokenProviderOptions> _tokenOptions = tokenOptions;

    public Task<bool> AvailableAsync(CancellationToken cancellationToken) =>
        _context.MailSettings.AnyAsync(s => s.Enabled, cancellationToken);

    public async Task<bool> SendPasswordLinkAsync(User user, string? sentBy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return false;
        var siteUrl = await _context.MailSettings.AsNoTracking()
            .Where(s => s.Enabled)
            .Select(s => s.SiteUrl)
            .SingleOrDefaultAsync(cancellationToken);
        if (siteUrl is null)
            return false;

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var kind = await _userManager.HasPasswordAsync(user) ? EmailKind.PasswordReset : EmailKind.SetPassword;
        return await _queue.QueueAsync(kind, user.Email, user.DisplayName, new
        {
            user.UserName,
            SentBy = sentBy,
            Link = $"{siteUrl}{SetPasswordPath}?user={user.Id}&token={Uri.EscapeDataString(token)}",
            LinkValidHours = (int)_tokenOptions.Value.TokenLifespan.TotalHours,
        }, cancellationToken);
    }

    /// <summary>Whether a link went to this address lately, so the anonymous form can't flood an inbox.</summary>
    public Task<bool> SentRecentlyAsync(string address, CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow - RequestCooldown;
        return _context.OutboundEmails.AnyAsync(
            e => e.ToAddress == address
                && e.CreatedAt > since
                && (e.Kind == EmailKind.PasswordReset || e.Kind == EmailKind.SetPassword),
            cancellationToken);
    }
}
