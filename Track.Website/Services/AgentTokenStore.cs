using System.Security.Cryptography;
using Track.Configuration;
using Microsoft.Extensions.Options;

namespace Track.Website.Services;

/// <summary>Agent: the random token that ties this node's name to it once an admin approves it.</summary>
public sealed class AgentTokenStore(IOptions<TrackOptions> options, ILogger<AgentTokenStore> logger)
{
    public const string Header = "X-Track-Agent-Token";

    private readonly Lazy<string> _token = new(() => LoadOrCreate(options.Value.AgentTokenPath, logger));

    public string Token => _token.Value;

    private static string LoadOrCreate(string path, ILogger logger)
    {
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: >= 32 } existing)
                return existing;

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, token);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return token;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Can't keep the agent token at {Path}; this node will need approving again after a restart", path);
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }
    }
}
