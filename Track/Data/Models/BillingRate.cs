using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>
/// The single-row pricing configuration used to turn utilization into dollars. Compute is billed as a
/// utility: paying customers collectively cover <see cref="ClusterComputeMonthlyCost"/> regardless of how
/// idle the cluster is. Storage is a flat per-GB markup on assigned usage.
/// </summary>
public class BillingRate : IEntityTypeConfiguration<BillingRate>
{
    /// <summary>Fixed key; there is only ever one row.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>What the whole cluster costs us per month; recovered in full from paying customers.</summary>
    public decimal ClusterComputeMonthlyCost { get; set; }

    /// <summary>Per-GB monthly rate for node-local disk. 0 treats local disk as bundled into the VM cost.</summary>
    public decimal DiskRatePerGbMonth { get; set; }

    /// <summary>Per-GB monthly rate for S3 object storage.</summary>
    public decimal ObjectStorageRatePerGbMonth { get; set; }

    public string Currency { get; set; } = "USD";

    public DateTime UpdatedUtc { get; set; }

    public void Configure(EntityTypeBuilder<BillingRate> builder)
    {
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Currency).HasMaxLength(8);
    }
}
