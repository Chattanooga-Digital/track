using Track.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

public class DiskSample : IEntityTypeConfiguration<DiskSample>
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public required string NodeName { get; set; }
    public DiskUsageKind Kind { get; set; }

    /// <summary>Container name for <see cref="DiskUsageKind.ContainerLayer"/>, volume name for <see cref="DiskUsageKind.Volume"/>.</summary>
    public required string Name { get; set; }

    public string? ContainerId { get; set; }
    public string? StackName { get; set; }
    public long SizeBytes { get; set; }

    public void Configure(EntityTypeBuilder<DiskSample> builder)
    {
        builder.Property(s => s.NodeName).HasMaxLength(100);
        builder.Property(s => s.Name).HasMaxLength(300);
        builder.Property(s => s.ContainerId).HasMaxLength(64);
        builder.Property(s => s.StackName).HasMaxLength(200);
        builder.HasIndex(s => s.Timestamp);
        builder.HasIndex(s => new { s.StackName, s.Timestamp });
    }
}
