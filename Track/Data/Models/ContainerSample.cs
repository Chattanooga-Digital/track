using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

public class ContainerSample : IEntityTypeConfiguration<ContainerSample>
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public required string NodeName { get; set; }
    public required string ContainerId { get; set; }
    public required string ContainerName { get; set; }
    public string? StackName { get; set; }
    public string? ServiceName { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryLimitBytes { get; set; }

    public void Configure(EntityTypeBuilder<ContainerSample> builder)
    {
        builder.Property(s => s.NodeName).HasMaxLength(100);
        builder.Property(s => s.ContainerId).HasMaxLength(64);
        builder.Property(s => s.ContainerName).HasMaxLength(300);
        builder.Property(s => s.StackName).HasMaxLength(200);
        builder.Property(s => s.ServiceName).HasMaxLength(300);
        builder.HasIndex(s => s.Timestamp);
        builder.HasIndex(s => new { s.StackName, s.Timestamp });
    }
}
