namespace Track.Website.Api.v0.Models.Auth;

public class CurrentUserModel
{
    public required string Username { get; set; }
    public required bool IsAdministrator { get; set; }
}
