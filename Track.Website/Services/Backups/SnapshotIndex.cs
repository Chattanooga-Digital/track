using System.Globalization;
using System.Text.Json;
using Track.Enums;
using Track.Data;
using Track.Data.Models;
using Track.Services.Backups;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Services.Backups;

/// <summary>Central: brings the snapshot index in line with what a repository really holds, after prune or on demand.</summary>
public sealed class SnapshotIndex(TrackContext context, ResticRunner restic)
{
    private readonly TrackContext _context = context;
    private readonly ResticRunner _restic = restic;

    public async Task SyncAsync(string repositoryKey, ResticTarget target, CancellationToken cancellationToken)
    {
        var result = await _restic.RunAsync(target, ["snapshots", "--json", "--tag", BackupSnapshot.Tag, "--tag", BackupSnapshot.PreRestoreTag], cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.Message);

        var listed = Parse(result.StandardOutput);
        var existing = await _context.BackupSnapshots.Where(s => s.RepositoryKey == repositoryKey).ToListAsync(cancellationToken);
        var byId = existing.ToDictionary(s => s.SnapshotId);

        _context.BackupSnapshots.RemoveRange(existing.Where(s => !listed.ContainsKey(s.SnapshotId)));
        foreach (var (id, snapshot) in listed)
        {
            if (byId.ContainsKey(id))
                continue;
            snapshot.RepositoryKey = repositoryKey;
            _context.BackupSnapshots.Add(snapshot);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public static Dictionary<string, BackupSnapshot> Parse(string json)
    {
        var snapshots = new Dictionary<string, BackupSnapshot>();
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = element.GetProperty("id").GetString()!;
            var paths = element.TryGetProperty("paths", out var p) ? p.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
            var tags = element.TryGetProperty("tags", out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
            var volume = paths.Select(VolumeOf).FirstOrDefault(v => v is not null);
            if (volume is null)
                continue;

            var prefix = $"/data/{volume}/";
            var dumpEngine = Tag(tags, BackupSnapshot.DumpTagPrefix) is { } engine
                && Enum.TryParse<DatabaseEngine>(engine, ignoreCase: true, out var parsed)
                    ? parsed
                    : (DatabaseEngine?)null;
            snapshots[id] = new BackupSnapshot
            {
                SnapshotId = id,
                RepositoryKey = "",
                VolumeKey = element.TryGetProperty("hostname", out var host) ? host.GetString() ?? volume : volume,
                VolumeName = volume,
                Time = ParseTime(element.GetProperty("time").GetString()) ?? DateTime.UtcNow,
                NodeName = Tag(tags, "node="),
                StackName = Tag(tags, "stack="),
                Paths = dumpEngine is not null
                    ? ""
                    : string.Join(',', paths.Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).Select(path => path[prefix.Length..])),
                DumpEngine = dumpEngine,
                PreRestore = tags.Contains(BackupSnapshot.PreRestoreTag),
                TotalBytes = element.TryGetProperty("summary", out var summary) && summary.TryGetProperty("total_bytes_processed", out var bytes)
                    ? bytes.GetInt64()
                    : null,
            };
        }

        return snapshots;
    }

    /// <summary>restic writes nanoseconds, which is more precision than the JSON reader accepts.</summary>
    public static DateTime? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;

    private static string? VolumeOf(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "data" ? parts[1] : null;
    }

    private static string? Tag(List<string> tags, string prefix) =>
        tags.FirstOrDefault(t => t.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}
