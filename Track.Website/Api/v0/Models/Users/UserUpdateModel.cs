namespace Track.Website.Api.v0.Models.Users;

public class UserUpdateModel
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsAdministrator { get; set; }
    public bool Disabled { get; set; }
}
