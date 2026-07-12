using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

public class User : IdentityUser<Guid>, IEntityTypeConfiguration<User>
{
    public DateTime CreatedAt { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>Disabled accounts cannot sign in; existing sessions are cut by rotating the security stamp.</summary>
    public bool Disabled { get; set; }

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
    }
}
