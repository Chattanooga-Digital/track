using Track.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>
/// A configured S3-compatible endpoint the central polls for per-bucket sizes. Managed live from the UI.
/// The secret key is stored encrypted (Data Protection); it is never returned to clients.
/// </summary>
public class S3Source : IEntityTypeConfiguration<S3Source>
{
    public int Id { get; set; }

    /// <summary>Stable identifier used as the bucket's source in assignments; unique, effectively immutable.</summary>
    public required string Name { get; set; }

    /// <summary>Service URL, e.g. http://minio:9000.</summary>
    public required string Endpoint { get; set; }

    public S3SourceKind Kind { get; set; }

    public required string AccessKey { get; set; }

    /// <summary>Data Protection-encrypted secret key. Decrypted only server-side at poll time.</summary>
    public required string SecretKeyProtected { get; set; }

    /// <summary>Signing region; MinIO ignores it. Defaults to us-east-1.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Disabled sources are kept but not polled.</summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public void Configure(EntityTypeBuilder<S3Source> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.HasIndex(s => s.Name).IsUnique();
        builder.Property(s => s.Endpoint).HasMaxLength(500);
        builder.Property(s => s.AccessKey).HasMaxLength(200);
        builder.Property(s => s.Region).HasMaxLength(64);
    }
}
