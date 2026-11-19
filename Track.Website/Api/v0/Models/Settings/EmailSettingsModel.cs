using Track.Enums;

namespace Track.Website.Api.v0.Models.Settings;

public class EmailSettingsModel
{
    public bool Configured { get; set; }
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public MailSecurity Security { get; set; }
    public string? UserName { get; set; }
    public bool HasPassword { get; set; }
    public string FromName { get; set; } = EmailSettingsEditModel.DefaultFromName;
    public string FromAddress { get; set; } = "";
    public string SiteUrl { get; set; } = "";
}

public class EmailSettingsEditModel
{
    public const string DefaultFromName = "Track";

    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public MailSecurity Security { get; set; }
    public string? UserName { get; set; }

    /// <summary>Blank keeps the stored password.</summary>
    public string? Password { get; set; }

    public string FromName { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string SiteUrl { get; set; } = "";
}

public class EmailTestModel : EmailSettingsEditModel
{
    public string To { get; set; } = "";
}

public class EmailTestResultModel
{
    public bool Success { get; set; }
    public required string Message { get; set; }
}
