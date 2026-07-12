using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>
/// One customer's usage at one billing tick, with its share of cluster capacity frozen at
/// record time. Billing history must be recorded as it happens: cluster capacity lives only
/// in memory and raw samples are pruned long before a billing period closes, and the
/// customer attribution here reflects the stack/bucket assignments as they were, not as
/// they are now. A null <see cref="CustomerId"/> is the unassigned (unbilled) remainder.
/// </summary>
public class BillingSample : IEntityTypeConfiguration<BillingSample>
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>Not a foreign key: history must survive customer deletion.</summary>
    public int? CustomerId { get; set; }

    /// <summary>Denormalized so a deleted customer's past bills still carry a name.</summary>
    public string? CustomerName { get; set; }

    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long DiskBytes { get; set; }
    public long ObjectStorageBytes { get; set; }
    public double CpuPercentOfCluster { get; set; }
    public double MemoryPercentOfCluster { get; set; }
    public double BlendedPercentOfCluster { get; set; }

    public void Configure(EntityTypeBuilder<BillingSample> builder)
    {
        builder.Property(s => s.CustomerName).HasMaxLength(200);
        builder.HasIndex(s => s.Timestamp);
        builder.HasIndex(s => new { s.CustomerId, s.Timestamp });
    }
}
