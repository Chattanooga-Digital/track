using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

public class StackAssignment : IEntityTypeConfiguration<StackAssignment>
{
    public int Id { get; set; }
    public required string StackName { get; set; }
    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public void Configure(EntityTypeBuilder<StackAssignment> builder)
    {
        builder.Property(a => a.StackName).HasMaxLength(200);
        builder.HasIndex(a => a.StackName).IsUnique();
        builder.HasOne(a => a.Customer)
               .WithMany(c => c.StackAssignments)
               .HasForeignKey(a => a.CustomerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
