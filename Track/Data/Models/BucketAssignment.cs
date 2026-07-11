using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>
/// Maps one S3 bucket (identified by its source and bucket name) to the customer billed for it.
/// The bucket equivalent of <see cref="StackAssignment"/>; a bucket belongs to at most one customer.
/// </summary>
public class BucketAssignment : IEntityTypeConfiguration<BucketAssignment>
{
    public int Id { get; set; }
    public required string SourceName { get; set; }
    public required string BucketName { get; set; }
    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public void Configure(EntityTypeBuilder<BucketAssignment> builder)
    {
        builder.Property(a => a.SourceName).HasMaxLength(100);
        builder.Property(a => a.BucketName).HasMaxLength(300);
        builder.HasIndex(a => new { a.SourceName, a.BucketName }).IsUnique();
        builder.HasOne(a => a.Customer)
               .WithMany(c => c.BucketAssignments)
               .HasForeignKey(a => a.CustomerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
