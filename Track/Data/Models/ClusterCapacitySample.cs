using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>
/// Cluster capacity at one billing tick. One row is written per tick alongside the
/// <see cref="BillingSample"/> rows, so a month's tick count (the averaging denominator)
/// and its average capacity come from here.
/// </summary>
public class ClusterCapacitySample : IEntityTypeConfiguration<ClusterCapacitySample>
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int NodeCount { get; set; }
    public long CpuCores { get; set; }
    public long MemoryBytes { get; set; }

    public void Configure(EntityTypeBuilder<ClusterCapacitySample> builder)
    {
        builder.HasIndex(s => s.Timestamp);
    }
}
