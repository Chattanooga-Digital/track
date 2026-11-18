using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Track.Data.Models;

/// <summary>Schedule, retention and alerting for backups. One row; times are minutes after midnight UTC.</summary>
public class BackupSettings : IEntityTypeConfiguration<BackupSettings>
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    // Starts paused so the first target save doesn't upload every volume before exclusions are reviewed.
    public bool Paused { get; set; } = true;
    public int BackupMinuteUtc { get; set; } = 2 * 60;
    public int SpreadMinutes { get; set; } = 180;
    public int MaxJobsPerNode { get; set; } = 1;
    public int MaxJobsTotal { get; set; } = 3;
    public int KeepDaily { get; set; } = 7;
    public int KeepWeekly { get; set; } = 4;
    public int KeepMonthly { get; set; } = 6;
    public DayOfWeek PruneDay { get; set; } = DayOfWeek.Sunday;

    // After the default backup window (02:00 plus 3 hours) so prune's exclusive lock doesn't collide with uploads.
    public int PruneMinuteUtc { get; set; } = 6 * 60;

    public int CheckPercent { get; set; } = 5;
    public int StaleAfterHours { get; set; } = 26;
    public int HookTimeoutMinutes { get; set; } = 15;
    public string? HeartbeatUrl { get; set; }
    public DateTime UpdatedAt { get; set; }

    public void Configure(EntityTypeBuilder<BackupSettings> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.HeartbeatUrl).HasMaxLength(500);
    }
}
