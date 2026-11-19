using System.Collections.Concurrent;
using Track.Data.Models;
using Track.Enums;
using RoushTech.Asio;

namespace Track.Website.Services.Backups;

/// <summary>Central: one Asio log session per backup job, which the job dialog watches live.</summary>
public sealed class JobLogs(JobSessionService sessions)
{
    private readonly JobSessionService _sessions = sessions;
    private readonly ConcurrentDictionary<Guid, byte> _started = new();

    public async Task StartAsync(BackupJob job)
    {
        // Creating a session again would wipe what it already holds.
        if (job.LogSessionId == Guid.Empty || !_started.TryAdd(job.LogSessionId, 0))
            return;
        await _sessions.CreateSession(job.LogSessionId, $"{job.Kind} {job.VolumeName ?? job.RepositoryKey}");
    }

    public async Task AppendAsync(BackupJob job, IEnumerable<(string Message, int Level)> lines)
    {
        await StartAsync(job);
        foreach (var (message, level) in lines)
            await _sessions.AppendLog(job.LogSessionId, message, level);
    }

    public async Task CompleteAsync(BackupJob job)
    {
        if (_started.TryRemove(job.LogSessionId, out _))
            await _sessions.CompleteSession(job.LogSessionId, job.Status != BackupJobStatus.Succeeded);
    }

    /// <summary>Runs <paramref name="work"/> with every log line it writes going to the job's session.</summary>
    public async Task InSessionAsync(BackupJob job, Func<Task> work)
    {
        await StartAsync(job);
        JobSessionService.ActivateSession(job.LogSessionId);
        await work();
    }
}
