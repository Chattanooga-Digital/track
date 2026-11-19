namespace Track.Website.Api.v0.Models.Users;

public class UserCreateModel
{
    public required string Username { get; set; }
    /// <summary>Unused when <see cref="SendPasswordEmail"/> is set; the user chooses their own.</summary>
    public string? Password { get; set; }

    public bool SendPasswordEmail { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsAdministrator { get; set; }
}
