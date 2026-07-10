namespace Track.Website.Api.v0.Models.Auth;

public class LoginModel
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public bool? RememberMe { get; set; }
}
