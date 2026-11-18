using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary><see cref="CustomerId"/> is not a foreign key, so a deleted customer's backups stay listed until retention ends.</summary>
public class BackupRepository : IEntityTypeConfiguration<BackupRepository>
{
    public const string SharedKey = "shared";
    public const string UnassignedKey = "unassigned";

    public int Id { get; set; }
    public required string Key { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public bool IsShared { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? InitializedAt { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? SizeUpdatedAt { get; set; }
    public DateTime? LastPrunedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public string? LastMaintenanceError { get; set; }

    public static string CustomerKey(int customerId) => $"customer-{customerId}";

    public void Configure(EntityTypeBuilder<BackupRepository> builder)
    {
        builder.Property(r => r.Key).HasMaxLength(64);
        builder.HasIndex(r => r.Key).IsUnique();
        builder.Property(r => r.CustomerName).HasMaxLength(200);
    }
}
