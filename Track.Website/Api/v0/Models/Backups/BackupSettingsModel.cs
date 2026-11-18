namespace Track.Website.Api.v0.Models.Backups;

public class BackupSettingsModel
{
    public bool Paused { get; set; }
    public int BackupMinuteUtc { get; set; }
    public int SpreadMinutes { get; set; }
    public int MaxJobsPerNode { get; set; }
    public int MaxJobsTotal { get; set; }
    public int KeepDaily { get; set; }
    public int KeepWeekly { get; set; }
    public int KeepMonthly { get; set; }
    public DayOfWeek PruneDay { get; set; }
    public int PruneMinuteUtc { get; set; }
    public int CheckPercent { get; set; }
    public int StaleAfterHours { get; set; }
    public int HookTimeoutMinutes { get; set; }
    public string? HeartbeatUrl { get; set; }
}
