using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>Where Track's choice wins over the stack's labels. No row means the labels decide.</summary>
public class BackupVolumeOverride : IEntityTypeConfiguration<BackupVolumeOverride>
{
    public int Id { get; set; }
    public required string VolumeName { get; set; }
    public bool Excluded { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public void Configure(EntityTypeBuilder<BackupVolumeOverride> builder)
    {
        builder.Property(o => o.VolumeName).HasMaxLength(255);
        builder.HasIndex(o => o.VolumeName).IsUnique();
        builder.Property(o => o.UpdatedBy).HasMaxLength(256);
    }
}
