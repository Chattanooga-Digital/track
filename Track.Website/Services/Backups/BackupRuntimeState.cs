using System.Collections.Concurrent;

namespace Track.Website.Services.Backups;

/// <summary>Central: in-memory backup state shared between requests and the background services.</summary>
public sealed class BackupRuntimeState
{
    private readonly ConcurrentDictionary<string, byte> _repositoriesInMaintenance = new();
    private readonly ConcurrentDictionary<string, DateTime> _maintenanceFailedAt = new();
    private volatile int _needsAttention;
    private volatile bool _maintenanceRequested;

    public SemaphoreSlim SchedulerGate { get; } = new(1, 1);

    public DateTime LastHeartbeat { get; set; }

    public int NeedsAttention
    {
        get => _needsAttention;
        set => _needsAttention = value;
    }

    public bool MaintenanceRequested
    {
        get => _maintenanceRequested;
        set => _maintenanceRequested = value;
    }

    public bool InMaintenance(string repositoryKey) => _repositoriesInMaintenance.ContainsKey(repositoryKey);

    public void EnterMaintenance(string repositoryKey) => _repositoriesInMaintenance[repositoryKey] = 0;

    public void ExitMaintenance(string repositoryKey) => _repositoriesInMaintenance.TryRemove(repositoryKey, out _);

    public DateTime? MaintenanceFailedAt(string repositoryKey) =>
        _maintenanceFailedAt.TryGetValue(repositoryKey, out var at) ? at : null;

    public void RecordMaintenanceResult(string repositoryKey, bool succeeded)
    {
        if (succeeded)
            _maintenanceFailedAt.TryRemove(repositoryKey, out _);
        else
            _maintenanceFailedAt[repositoryKey] = DateTime.UtcNow;
    }
}
