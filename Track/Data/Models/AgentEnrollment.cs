using Track.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

public class AgentEnrollment : IEntityTypeConfiguration<AgentEnrollment>
{
    public int Id { get; set; }
    public required string NodeName { get; set; }
    public required string TokenHash { get; set; }

    /// <summary>A different token presented under an approved name, waiting for an admin to accept it.</summary>
    public string? PendingTokenHash { get; set; }

    public AgentApproval Status { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }

    public void Configure(EntityTypeBuilder<AgentEnrollment> builder)
    {
        builder.Property(e => e.NodeName).HasMaxLength(200);
        builder.HasIndex(e => e.NodeName).IsUnique();
        builder.Property(e => e.TokenHash).HasMaxLength(64);
        builder.Property(e => e.PendingTokenHash).HasMaxLength(64);
        builder.Property(e => e.ApprovedBy).HasMaxLength(256);
    }
}
