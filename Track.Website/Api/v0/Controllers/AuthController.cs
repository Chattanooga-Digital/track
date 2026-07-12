using Track.Data.Models;
using Track.Website.Api.v0.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Track.Website.Api.v0.Controllers;

[ApiController]
[Route("/api/v0/auth")]
public class AuthController(
    ILogger<AuthController> logger,
    SignInManager<User> signInManager,
    UserManager<User> userManager
) : ControllerBase
{
    protected ILogger<AuthController> Logger { get; } = logger;
    protected SignInManager<User> SignInManager { get; } = signInManager;
    protected UserManager<User> UserManager { get; } = userManager;

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

    private async Task<CurrentUserModel> BuildCurrentUserAsync(User user) => new()
    {
        Username = user.UserName!,
        IsAdministrator = await UserManager.IsInRoleAsync(user, Role.Administrator),
    };
}
