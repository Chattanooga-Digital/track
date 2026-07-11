using Track.Configuration;
using Microsoft.Extensions.Options;

namespace Track.Services.Docker;

/// <summary>
/// Holds the agent's current ordered env-var identity list. Seeded from <see cref="TrackOptions.GroupingEnvVars"/>,
/// then replaced at runtime by whatever the central returns in the ingest push response. Thread-safe via a
/// volatile swap of an immutable list (read on every poll, written once per push).
/// </summary>
public sealed class IdentityEnvVarProvider(IOptions<TrackOptions> options)
{
    private volatile IReadOnlyList<string> _current = [.. options.Value.GroupingEnvVars];

    public IReadOnlyList<string> Current => _current;

    /// <summary>Replace the list with the central's authoritative copy. Null clears it.</summary>
    public void Update(IEnumerable<string>? identityEnvVars) =>
        _current = identityEnvVars is null ? [] : [.. identityEnvVars];
}
