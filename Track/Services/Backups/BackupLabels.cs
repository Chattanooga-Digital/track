namespace Track.Services.Backups;

/// <summary>Reads backup-bot-two's label names. Backups are on by default, so only a false value changes anything.</summary>
public static class BackupLabels
{
    public const string Prefix = "backupbot.";
    public const string Backup = "backupbot.backup";
    public const string PreHook = "backupbot.backup.pre-hook";
    public const string PostHook = "backupbot.backup.post-hook";
    public const string RestorePreHook = "backupbot.restore.pre-hook";
    public const string RestorePostHook = "backupbot.restore.post-hook";

    private const string VolumesPrefix = "backupbot.backup.volumes.";

    public static Dictionary<string, string> Select(IDictionary<string, string>? labels) =>
        labels?.Where(l => l.Key.StartsWith(Prefix, StringComparison.Ordinal)).ToDictionary(l => l.Key, l => l.Value) ?? [];

    public static bool ExcludesService(IReadOnlyDictionary<string, string> labels) =>
        labels.TryGetValue(Backup, out var value) && IsFalse(value);

    public static bool ExcludesVolume(IReadOnlyDictionary<string, string> labels, string volumeName, string? stackName) =>
        Names(volumeName, stackName).Any(name =>
            labels.TryGetValue(VolumesPrefix + name, out var value) && IsFalse(value));

    /// <summary>Paths inside the volume to back up, relative to its root. Empty means the whole volume.</summary>
    public static List<string> Paths(IReadOnlyDictionary<string, string> labels, string volumeName, string? stackName)
    {
        foreach (var name in Names(volumeName, stackName))
        {
            if (!labels.TryGetValue($"{VolumesPrefix}{name}.path", out var value))
                continue;

            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Trim('/'))
                .Where(p => p.Length > 0 && !p.Split('/').Contains(".."))
                .Distinct()
                .ToList();
        }

        return [];
    }

    public static string? Hook(IReadOnlyDictionary<string, string> labels, string key) =>
        labels.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    // backup-bot-two's docs don't say whether the label takes the full volume name or the compose key, so accept both.
    private static IEnumerable<string> Names(string volumeName, string? stackName)
    {
        yield return volumeName;
        if (stackName is not null && volumeName.StartsWith(stackName + "_", StringComparison.Ordinal))
            yield return volumeName[(stackName.Length + 1)..];
    }

    private static bool IsFalse(string value) =>
        value.Trim().ToLowerInvariant() is "false" or "0" or "no" or "off";
}
