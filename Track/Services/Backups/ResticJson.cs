using System.Text.Json;

namespace Track.Services.Backups;

public sealed record ResticStatus(double? PercentDone, long? BytesDone, long? TotalBytes);

public sealed record ResticSummary(
    string? SnapshotId, long? BytesAdded, long? FilesNew, long? FilesChanged, long? TotalBytes);

/// <summary>Reads the JSON lines restic prints with <c>--json</c> for backup and restore.</summary>
public static class ResticJson
{
    public static object? ParseLine(string line)
    {
        if (line.Length == 0 || line[0] != '{')
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("message_type", out var type))
                return null;

            return type.GetString() switch
            {
                "status" => new ResticStatus(
                    Double(root, "percent_done"),
                    Long(root, "bytes_done") ?? Long(root, "bytes_restored"),
                    Long(root, "total_bytes")),
                "summary" => new ResticSummary(
                    String(root, "snapshot_id"),
                    Long(root, "data_added_packed") ?? Long(root, "data_added"),
                    Long(root, "files_new") ?? Long(root, "files_restored"),
                    Long(root, "files_changed"),
                    Long(root, "total_bytes_processed") ?? Long(root, "total_bytes")),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? Double(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static long? Long(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
