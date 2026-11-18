using Track.Services.Ingest;

namespace Track.Services;

/// <summary>The latest swarm view from any manager agent.</summary>
public sealed class SwarmInventoryHolder
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly object _lock = new();
    private SwarmReport? _latest;

    public void Set(SwarmReport report)
    {
        lock (_lock)
        {
            if (_latest is null || report.Timestamp >= _latest.Timestamp)
                _latest = report;
        }
    }

    public SwarmReport? Get(DateTime nowUtc)
    {
        lock (_lock)
            return _latest is not null && nowUtc - _latest.Timestamp <= MaxAge ? _latest : null;
    }
}
