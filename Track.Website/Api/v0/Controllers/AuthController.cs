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
        var result = await SignInManager.PasswordSignInAsync(
            model.Username, model.Password, model.RememberMe ?? true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await UserManager.FindByNameAsync(model.Username);
            return user is null ? Unauthorized() : Ok(new CurrentUserModel { Username = user.UserName! });
        }

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

        return Ok(new CurrentUserModel { Username = user.UserName! });
    }
}
