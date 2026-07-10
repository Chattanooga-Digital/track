using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Track.Data;

/// <summary>
/// Design-time factory so `dotnet ef` can build a <see cref="TrackContext"/> without running
/// the Website's startup (which would trigger seeding). Only used by the EF tools; the running
/// app configures its own context through <c>AddTrack</c>.
/// </summary>
public class TrackContextFactory : IDesignTimeDbContextFactory<TrackContext>
{
    public TrackContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TrackContext>()
            .UseSqlite("Data Source=track.db")
            .Options;

        return new TrackContext(options);
    }
}
