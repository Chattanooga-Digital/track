using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Track.Services.Mail;
using Track.Website.Api.v0.Models.Settings;
using MimeKit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

[ApiController]
[Authorize(Roles = Role.Administrator)]
[Route("/api/v0/settings/email")]
public class EmailSettingsController(
    TrackContext trackContext,
    MailSecretProtector secretProtector,
    EmailRenderer renderer,
    IMailTransport transport) : ControllerBase
{
    protected TrackContext TrackContext { get; } = trackContext;
    protected MailSecretProtector SecretProtector { get; } = secretProtector;
    protected EmailRenderer Renderer { get; } = renderer;
    protected IMailTransport Transport { get; } = transport;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var settings = await TrackContext.MailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
            return Ok(new EmailSettingsModel());

        return Ok(new EmailSettingsModel
        {
            Configured = true,
            Enabled = settings.Enabled,
            Host = settings.Host,
            Port = settings.Port,
            Security = settings.Security,
            UserName = settings.UserName,
            HasPassword = !string.IsNullOrEmpty(settings.PasswordProtected),
            FromName = settings.FromName,
            FromAddress = settings.FromAddress,
            SiteUrl = settings.SiteUrl,
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] EmailSettingsEditModel model, CancellationToken cancellationToken)
    {
        if (Invalid(model) is { } error)
            return BadRequest(error);

        var settings = await TrackContext.MailSettings.SingleOrDefaultAsync(cancellationToken)
            ?? TrackContext.MailSettings.Add(new MailSettings { Host = "", FromName = "", FromAddress = "", SiteUrl = "" }).Entity;
        settings.Enabled = model.Enabled;
        settings.Host = model.Host.Trim();
        settings.Port = model.Port;
        settings.Security = model.Security;
        settings.UserName = string.IsNullOrWhiteSpace(model.UserName) ? null : model.UserName.Trim();
        if (settings.UserName is null)
            settings.PasswordProtected = null;
        else if (!string.IsNullOrEmpty(model.Password))
            settings.PasswordProtected = SecretProtector.Protect(model.Password);
        settings.FromName = FromName(model);
        settings.FromAddress = model.FromAddress.Trim();
        settings.SiteUrl = SiteUrl(model);
        settings.UpdatedAt = DateTime.UtcNow;

        await TrackContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] EmailTestModel model, CancellationToken cancellationToken)
    {
        if (Invalid(model) is { } error)
            return BadRequest(error);
        var to = model.To.Trim();
        if (!IsAddress(to))
            return BadRequest("Enter the address to send the test to.");

        var userName = string.IsNullOrWhiteSpace(model.UserName) ? null : model.UserName.Trim();
        var password = model.Password;
        if (userName is not null && string.IsNullOrEmpty(password))
        {
            var stored = await TrackContext.MailSettings.AsNoTracking()
                .Select(s => s.PasswordProtected)
                .SingleOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrEmpty(stored))
                password = SecretProtector.Unprotect(stored);
        }

        var settings = new SmtpSettings(
            model.Host.Trim(), model.Port, model.Security, userName, password, FromName(model), model.FromAddress.Trim());
        var email = await Renderer.RenderAsync(EmailKind.Test, new { SentBy = User.Identity?.Name, settings.Host });
        try
        {
            await Transport.SendAsync(settings, to, null, email, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Ok(new EmailTestResultModel { Success = false, Message = ex.Message });
        }

        return Ok(new EmailTestResultModel { Success = true, Message = $"Sent to {to}." });
    }

    private static string? Invalid(EmailSettingsEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Host))
            return "SMTP host is required.";
        if (model.Port is < 1 or > 65535)
            return "Port must be between 1 and 65535.";
        if (!IsAddress(model.FromAddress.Trim()))
            return "From address must be an email address.";
        if (!Uri.TryCreate(SiteUrl(model), UriKind.Absolute, out var site) || site.Scheme is not ("http" or "https"))
            return "Site URL must be a full address, such as https://track.example.com.";
        return null;
    }

    private static string SiteUrl(EmailSettingsEditModel model) => model.SiteUrl.Trim().TrimEnd('/');

    private static string FromName(EmailSettingsEditModel model) =>
        string.IsNullOrWhiteSpace(model.FromName) ? EmailSettingsEditModel.DefaultFromName : model.FromName.Trim();

    // MimeKit accepts a bare local part or a display name too; only a plain user@domain is wanted here.
    private static bool IsAddress(string value) =>
        MailboxAddress.TryParse(value, out var mailbox) && mailbox.Address == value && value.Contains('@');
}
