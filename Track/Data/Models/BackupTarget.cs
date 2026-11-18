using Track.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>The off-site bucket backups go to. Kept apart from <see cref="S3Source"/>: these keys are handed to agents.</summary>
public class BackupTarget : IEntityTypeConfiguration<BackupTarget>
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public required string Endpoint { get; set; }
    public string Region { get; set; } = "us-east-1";
    public required string Bucket { get; set; }
    public string Prefix { get; set; } = "track/";
    public S3Addressing Addressing { get; set; }
    public required string AgentAccessKey { get; set; }
    public required string AgentSecretKeyProtected { get; set; }
    public string? MaintenanceAccessKey { get; set; }
    public string? MaintenanceSecretKeyProtected { get; set; }
    public DateTime UpdatedAt { get; set; }

    public void Configure(EntityTypeBuilder<BackupTarget> builder)
    {
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Endpoint).HasMaxLength(500);
        builder.Property(t => t.Region).HasMaxLength(64);
        builder.Property(t => t.Bucket).HasMaxLength(200);
        builder.Property(t => t.Prefix).HasMaxLength(200);
        builder.Property(t => t.AgentAccessKey).HasMaxLength(200);
        builder.Property(t => t.MaintenanceAccessKey).HasMaxLength(200);
    }
}
