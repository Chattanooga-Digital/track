using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>
/// An environment-variable name the operator chose to use as a grouping identity for label-less standalone
/// containers. Agents receive the ordered list and group a container by the value of the first one present.
/// </summary>
public class IdentityEnvVar : IEntityTypeConfiguration<IdentityEnvVar>
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Priority order; lower is checked first.</summary>
    public int SortOrder { get; set; }

    public void Configure(EntityTypeBuilder<IdentityEnvVar> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.HasIndex(e => e.Name).IsUnique();
    }
}
