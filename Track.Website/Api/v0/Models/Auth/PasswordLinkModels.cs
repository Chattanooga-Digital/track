namespace Track.Website.Api.v0.Models.Auth;

public class AuthOptionsModel
{
    public bool PasswordReset { get; set; }
}

public class ForgotPasswordModel
{
    public string Login { get; set; } = "";
}

public class SetPasswordWithTokenModel
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = "";
    public string Password { get; set; } = "";
}
