namespace Track.Website.Api.v0.Models.Auth;

public class ChangePasswordModel
{
    public required string CurrentPassword { get; set; }
    public required string NewPassword { get; set; }
}
