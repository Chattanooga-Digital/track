using Track.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>The SMTP server outbound email goes through. One row; none means mail is off.</summary>
public class MailSettings : IEntityTypeConfiguration<MailSettings>
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool Enabled { get; set; } = true;
    public required string Host { get; set; }
    public int Port { get; set; } = 587;
    public MailSecurity Security { get; set; }
    public string? UserName { get; set; }
    public string? PasswordProtected { get; set; }
    public required string FromName { get; set; }
    public required string FromAddress { get; set; }

    /// <summary>Where emailed links point. Never taken from a request, whose Host header anyone can set.</summary>
    public required string SiteUrl { get; set; }

    public DateTime UpdatedAt { get; set; }

    public void Configure(EntityTypeBuilder<MailSettings> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Host).HasMaxLength(255);
        builder.Property(s => s.UserName).HasMaxLength(255);
        builder.Property(s => s.FromName).HasMaxLength(200);
        builder.Property(s => s.FromAddress).HasMaxLength(320);
        builder.Property(s => s.SiteUrl).HasMaxLength(500);
    }
}
