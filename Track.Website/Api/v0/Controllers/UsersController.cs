using Track.Data.Models;
using Track.Website.Api.v0.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

/// <summary>
/// Administrator-only account management. Guards keep the system reachable: you cannot disable, demote,
/// or delete your own account, and the last active administrator cannot be disabled, demoted, or deleted.
/// </summary>
[ApiController]
[Authorize(Roles = Role.Administrator)]
[Route("/api/v0/users")]
public class UsersController(
    ILogger<UsersController> logger,
    UserManager<User> userManager,
    SignInManager<User> signInManager) : ControllerBase
{
    protected ILogger<UsersController> Logger { get; } = logger;
    protected UserManager<User> UserManager { get; } = userManager;
    protected SignInManager<User> SignInManager { get; } = signInManager;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var adminIds = (await UserManager.GetUsersInRoleAsync(Role.Administrator))
            .Select(u => u.Id)
            .ToHashSet();

        var users = await UserManager.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.DisplayName,
                u.Disabled,
                u.LockoutEnd,
                u.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return Ok(users.Select(u => new UserModel
        {
            Id = u.Id,
            Username = u.UserName!,
            Email = u.Email,
            DisplayName = u.DisplayName,
            IsAdministrator = adminIds.Contains(u.Id),
            Disabled = u.Disabled,
            LockedOut = u.LockoutEnd > DateTimeOffset.UtcNow,
            CreatedAt = u.CreatedAt,
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserCreateModel model)
    {
        var username = model.Username.Trim();
        if (username.Length == 0)
            return BadRequest("Username is required.");

        var user = new User
        {
            UserName = username,
            Email = string.IsNullOrWhiteSpace(model.Email) ? $"{username}@localhost" : model.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName.Trim(),
            CreatedAt = DateTime.UtcNow,
        };

        var result = await UserManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
            return BadRequest(IdentityErrors(result));

        if (model.IsAdministrator)
            await UserManager.AddToRoleAsync(user, Role.Administrator);

        Logger.LogInformation("User '{Username}' created by '{Actor}'.", username, User.Identity?.Name);

        return Ok(new UserModel
        {
            Id = user.Id,
            Username = user.UserName!,
            Email = user.Email,
            DisplayName = user.DisplayName,
            IsAdministrator = model.IsAdministrator,
            Disabled = false,
            LockedOut = false,
            CreatedAt = user.CreatedAt,
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UserUpdateModel model)
    {
        var user = await UserManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var isSelf = id == GetCurrentUserId();
        if (isSelf && (model.Disabled || !model.IsAdministrator))
            return BadRequest("You cannot disable or demote your own account.");

        var wasAdministrator = await UserManager.IsInRoleAsync(user, Role.Administrator);
        var losesAdmin = wasAdministrator && (!model.IsAdministrator || model.Disabled);
        if (losesAdmin && await CountOtherActiveAdminsAsync(user) == 0)
            return BadRequest("There must be at least one active administrator.");

        var disabling = model.Disabled && !user.Disabled;

        user.Email = string.IsNullOrWhiteSpace(model.Email) ? user.Email : model.Email.Trim();
        user.DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName.Trim();
        user.Disabled = model.Disabled;

        var result = await UserManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(IdentityErrors(result));

        if (model.IsAdministrator && !wasAdministrator)
            await UserManager.AddToRoleAsync(user, Role.Administrator);
        else if (!model.IsAdministrator && wasAdministrator)
            await UserManager.RemoveFromRoleAsync(user, Role.Administrator);

        // Cut existing sessions when access is reduced; the cookie dies on the next security-stamp check.
        if (disabling || (wasAdministrator && !model.IsAdministrator))
            await UserManager.UpdateSecurityStampAsync(user);

        return Ok();
    }

    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> SetPassword([FromRoute] Guid id, [FromBody] SetPasswordModel model)
    {
        var user = await UserManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        // Reset-token flow so the current password is not needed; it also rotates the security stamp,
        // signing the user out everywhere.
        var token = await UserManager.GeneratePasswordResetTokenAsync(user);
        var result = await UserManager.ResetPasswordAsync(user, token, model.Password);
        if (!result.Succeeded)
            return BadRequest(IdentityErrors(result));

        if (id == GetCurrentUserId())
            await SignInManager.RefreshSignInAsync(user);

        Logger.LogInformation("Password for '{Username}' set by '{Actor}'.", user.UserName, User.Identity?.Name);
        return Ok();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var user = await UserManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        if (id == GetCurrentUserId())
            return BadRequest("You cannot delete your own account.");

        if (await UserManager.IsInRoleAsync(user, Role.Administrator) &&
            await CountOtherActiveAdminsAsync(user) == 0)
            return BadRequest("There must be at least one active administrator.");

        var result = await UserManager.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(IdentityErrors(result));

        Logger.LogInformation("User '{Username}' deleted by '{Actor}'.", user.UserName, User.Identity?.Name);
        return Ok();
    }

    private Guid GetCurrentUserId() => Guid.Parse(UserManager.GetUserId(User)!);

    private async Task<int> CountOtherActiveAdminsAsync(User target)
    {
        var admins = await UserManager.GetUsersInRoleAsync(Role.Administrator);
        return admins.Count(a => a.Id != target.Id && !a.Disabled);
    }

    private static string IdentityErrors(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));
}
