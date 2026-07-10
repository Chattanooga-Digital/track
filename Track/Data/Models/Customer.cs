using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

public class Customer : IEntityTypeConfiguration<Customer>
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<StackAssignment> StackAssignments { get; set; } = [];

    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.HasIndex(c => c.Name).IsUnique();
    }
}
