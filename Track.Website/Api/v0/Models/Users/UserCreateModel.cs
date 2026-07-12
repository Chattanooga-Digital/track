namespace Track.Website.Api.v0.Models.Users;

public class UserCreateModel
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsAdministrator { get; set; }
}
