using System.Security.Cryptography;
using System.Text;

namespace Track.Services.Backups;

public static class BackupSchedule
{
    /// <summary>A stable spot inside the window for each volume, so a night's jobs don't all start at once.</summary>
    public static int OffsetMinutes(string key, int spreadMinutes)
    {
        if (spreadMinutes <= 0)
            return 0;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return (int)(BitConverter.ToUInt32(hash, 0) % (uint)spreadMinutes);
    }

    public static DateTime MostRecentDailySlot(DateTime nowUtc, int minuteOfDayUtc, int offsetMinutes)
    {
        var slot = nowUtc.Date.AddMinutes(minuteOfDayUtc + offsetMinutes);
        while (slot > nowUtc)
            slot = slot.AddDays(-1);
        return slot;
    }

    public static DateTime MostRecentWeeklySlot(DateTime nowUtc, DayOfWeek day, int minuteOfDayUtc)
    {
        var slot = nowUtc.Date.AddDays(-(((int)nowUtc.DayOfWeek - (int)day + 7) % 7)).AddMinutes(minuteOfDayUtc);
        return slot > nowUtc ? slot.AddDays(-7) : slot;
    }
}
