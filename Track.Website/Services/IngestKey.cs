using System.Security.Cryptography;
using System.Text;
using Track.Configuration;

namespace Track.Website.Services;

public static class IngestKey
{
    /// <summary>Constant-time check of the shared ingest secret. Ingest is off until Track:Ingest:Key is set.</summary>
    public static bool IsValid(HttpRequest request, TrackOptions options)
    {
        var expected = options.Ingest.Key;
        if (string.IsNullOrEmpty(expected))
            return false;

        if (!request.Headers.TryGetValue(CentralClient.KeyHeader, out var provided) || provided.Count != 1)
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided[0]!), Encoding.UTF8.GetBytes(expected));
    }

    public static string? AgentToken(HttpRequest request) =>
        request.Headers.TryGetValue(AgentTokenStore.Header, out var token) && token.Count == 1 ? token[0] : null;
}
