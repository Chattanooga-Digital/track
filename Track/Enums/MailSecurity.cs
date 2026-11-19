namespace Track.Enums;

/// <summary>Auto uses TLS on connect for port 465 and STARTTLS whenever the server offers it otherwise.</summary>
public enum MailSecurity
{
    Auto = 0,
    StartTls = 1,
    SslOnConnect = 2,
    None = 3,
}
