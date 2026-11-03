using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>
/// An S3 bucket that serves the whole cluster (backups, platform data) rather than one customer. The bucket
/// equivalent of <see cref="SharedStack"/>: reported as shared infrastructure and never billed.
/// </summary>
public class SharedBucket : IEntityTypeConfiguration<SharedBucket>
{
    public int Id { get; set; }
    public required string SourceName { get; set; }
    public required string BucketName { get; set; }

    public void Configure(EntityTypeBuilder<SharedBucket> builder)
    {
        builder.Property(b => b.SourceName).HasMaxLength(100);
        builder.Property(b => b.BucketName).HasMaxLength(300);
        builder.HasIndex(b => new { b.SourceName, b.BucketName }).IsUnique();
    }
}
