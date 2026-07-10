using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

public class User : IdentityUser<Guid>, IEntityTypeConfiguration<User>
{
    public DateTime CreatedAt { get; set; }
    public string? DisplayName { get; set; }

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
    }
}
