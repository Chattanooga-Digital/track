namespace Track.Website.Api.v0.Models.Users;

public class UserModel
{
    public required Guid Id { get; set; }
    public required string Username { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public required bool IsAdministrator { get; set; }
    public required bool Disabled { get; set; }
    public required bool LockedOut { get; set; }
    public required DateTime CreatedAt { get; set; }
}
