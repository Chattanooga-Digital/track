namespace Track.Services.Docker;

/// <summary>
/// Recognises environment-variable names that likely hold secrets, so they are neither surfaced as
/// grouping candidates nor allowed as an identity. Picking a secret as an identity would send its value
/// to the central as a stack name - exactly the leak this guards against.
/// </summary>
public static class SensitiveEnvKeys
{
    /// <summary>Case-insensitive substrings that mark a key name as sensitive.</summary>
    public static readonly IReadOnlyList<string> Markers =
    [
        "pass", "pwd", "secret", "token", "key", "auth", "credential", "private", "cert", "salt", "dsn",
    ];

    public static bool IsSensitive(string name) =>
        Markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
