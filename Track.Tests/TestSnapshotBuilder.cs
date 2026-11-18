using Track.Data;
using Track.Services;
using Track.Website.Queries;
using Track.Website.Services.Backups;
using Microsoft.Extensions.DependencyInjection;

namespace Track.Tests;

internal static class TestSnapshotBuilder
{
    public static SnapshotBuilder Create(TrackContext context, CurrentStateHolder holder) =>
        new(context, holder,
            new AgentEnrollmentRegistry(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>()),
            new BackupRuntimeState());
}
