namespace Track.Services;

/// <summary>
/// Central in-memory cache of the ordered env-var identity list (persisted in the IdentityEnvVars table).
/// Read on every stats push to stamp the agent config response, so it avoids a DB hit per push. Loaded at
/// startup and refreshed when the grouping config is edited.
/// </summary>
public sealed class IdentityEnvVarStore
{
    private volatile IReadOnlyList<string> _current = [];

    public IReadOnlyList<string> Current => _current;

    public void Set(IEnumerable<string> names) => _current = [.. names];
}
