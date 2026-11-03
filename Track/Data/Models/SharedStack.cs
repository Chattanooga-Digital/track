using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>
/// A stack that serves the whole cluster (proxy, orchestration, monitoring) rather than one customer. It is reported
/// as shared infrastructure and never billed; its share is recovered through what customers pay.
/// </summary>
public class SharedStack : IEntityTypeConfiguration<SharedStack>
{
    public int Id { get; set; }
    public required string StackName { get; set; }

    public void Configure(EntityTypeBuilder<SharedStack> builder)
    {
        builder.Property(s => s.StackName).HasMaxLength(200);
        builder.HasIndex(s => s.StackName).IsUnique();
    }
}
