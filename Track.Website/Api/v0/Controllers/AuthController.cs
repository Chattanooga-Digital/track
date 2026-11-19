using Track.Data.Models;
using Track.Website.Api.v0.Models.Auth;
using Track.Website.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

[ApiController]
[Route("/api/v0/auth")]
public class AuthController(
    ILogger<AuthController> logger,
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    AccountEmails accountEmails
) : ControllerBase
{
    private const string LinkNoLongerWorks = "This link has expired or was already used. Ask for a new one.";

    protected ILogger<AuthController> Logger { get; } = logger;
    protected SignInManager<User> SignInManager { get; } = signInManager;
    protected UserManager<User> UserManager { get; } = userManager;
    protected AccountEmails AccountEmails { get; } = accountEmails;

    [HttpGet("options")]
    [AllowAnonymous]
    public async Task<IActionResult> Options(CancellationToken cancellationToken) =>
        Ok(new AuthOptionsModel { PasswordReset = await AccountEmails.AvailableAsync(cancellationToken) });

    /// <summary>Answers the same whether or not the account exists, so it can't be used to find usernames.</summary>
    [HttpPost("forgot")]
    [AllowAnonymous]
    public async Task<IActionResult> Forgot([FromBody] ForgotPasswordModel model, CancellationToken cancellationToken)
    {
        var login = model.Login.Trim();
        if (login.Length == 0)
            return BadRequest("Enter your username or email.");

        foreach (var user in await FindByLoginAsync(login, cancellationToken))
        {
            if (user.Disabled || string.IsNullOrWhiteSpace(user.Email)
                || await AccountEmails.SentRecentlyAsync(user.Email, cancellationToken))
                continue;

            if (await AccountEmails.SendPasswordLinkAsync(user, sentBy: null, cancellationToken))
                Logger.LogInformation("Password link requested for '{Username}'.", user.UserName);
        }

        return NoContent();
    }

    [HttpPost("set-password")]
    [AllowAnonymous]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordWithTokenModel model)
    {
        var user = await UserManager.FindByIdAsync(model.UserId.ToString());
        if (user is null || user.Disabled)
            return BadRequest(LinkNoLongerWorks);

        var result = await UserManager.ResetPasswordAsync(user, model.Token, model.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? LinkNoLongerWorks
                : string.Join(" ", result.Errors.Select(e => e.Description)));

        // Owning the inbox is proof enough; a lockout from someone's wrong guesses shouldn't block the new password.
        await UserManager.SetLockoutEndDateAsync(user, null);
        await UserManager.ResetAccessFailedCountAsync(user);
        await SignInManager.SignInAsync(user, isPersistent: true);

        Logger.LogInformation("Password for '{Username}' set from an emailed link.", user.UserName);
        return Ok(await BuildCurrentUserAsync(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginModel model)
    {
        // A disabled account looks like a bad login; don't reveal that the username exists.
        var user = await UserManager.FindByNameAsync(model.Username);
        if (user is null || user.Disabled)
            return Unauthorized();

        var result = await SignInManager.PasswordSignInAsync(
            user, model.Password, model.RememberMe ?? true, lockoutOnFailure: true);

        if (result.Succeeded)
            return Ok(await BuildCurrentUserAsync(user));

        if (result.IsLockedOut)
            return BadRequest(new { error = "AccountLockedOut" });

        return Unauthorized();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await SignInManager.SignOutAsync();
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await UserManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        if (user.Disabled)
        {
            await SignInManager.SignOutAsync();
            return Unauthorized();
        }

        return Ok(await BuildCurrentUserAsync(user));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordModel model)
    {
        var user = await UserManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var result = await UserManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
            return BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

        // The password change rotated the security stamp; reissue the cookie so this session survives.
        await SignInManager.RefreshSignInAsync(user);
        return Ok();
    }

    private async Task<List<User>> FindByLoginAsync(string login, CancellationToken cancellationToken)
    {
        if (await UserManager.FindByNameAsync(login) is { } byName)
            return [byName];

        // Emails aren't unique, so FindByEmailAsync would throw on a shared address.
        var normalized = UserManager.NormalizeEmail(login);
        return await UserManager.Users.Where(u => u.NormalizedEmail == normalized).ToListAsync(cancellationToken);
    }

    private async Task<CurrentUserModel> BuildCurrentUserAsync(User user) => new()
    {
        Username = user.UserName!,
        IsAdministrator = await UserManager.IsInRoleAsync(user, Role.Administrator),
        Email = user.Email,
    };
}
