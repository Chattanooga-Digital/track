using Track.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>An email rendered when it was queued, kept until it is sent or gives up.</summary>
public class OutboundEmail : IEntityTypeConfiguration<OutboundEmail>
{
    public int Id { get; set; }
    public EmailKind Kind { get; set; }
    public required string ToAddress { get; set; }
    public string? ToName { get; set; }
    public required string Subject { get; set; }
    public required string HtmlBody { get; set; }
    public EmailStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? LastError { get; set; }

    public void Configure(EntityTypeBuilder<OutboundEmail> builder)
    {
        builder.Property(e => e.ToAddress).HasMaxLength(320);
        builder.Property(e => e.ToName).HasMaxLength(200);
        builder.Property(e => e.Subject).HasMaxLength(500);
        builder.Property(e => e.LastError).HasMaxLength(1000);
        builder.HasIndex(e => new { e.Status, e.NextAttemptAt });
        builder.HasIndex(e => e.CreatedAt);
    }
}
