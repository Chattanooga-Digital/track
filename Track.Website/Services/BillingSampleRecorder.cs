using Track.Data;
using Track.Data.Models;
using Track.Website.Queries;

namespace Track.Website.Services;

/// <summary>
/// Records one billing tick: the current per-customer cluster shares (from the live snapshot)
/// plus the cluster capacity they were measured against. Monthly billing averages these ticks,
/// so each row freezes both the capacity and the customer attribution as they were at record
/// time - neither can be reconstructed later.
/// </summary>
public class BillingSampleRecorder(TrackContext trackContext, SnapshotBuilder snapshotBuilder)
{
    private readonly TrackContext _trackContext = trackContext;
    private readonly SnapshotBuilder _snapshotBuilder = snapshotBuilder;

    /// <summary>Returns false when nothing was recorded (no online nodes means no capacity to bill against).</summary>
    public async Task<bool> RecordAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotBuilder.BuildAsync(cancellationToken);
        if (snapshot.Cluster.NodeCount == 0)
            return false;

        _trackContext.ClusterCapacitySamples.Add(new ClusterCapacitySample
        {
            Timestamp = snapshot.Timestamp,
            NodeCount = snapshot.Cluster.NodeCount,
            CpuCores = snapshot.Cluster.CpuCores,
            MemoryBytes = snapshot.Cluster.MemoryBytes,
        });

        foreach (var customer in snapshot.Customers)
        {
            _trackContext.BillingSamples.Add(new BillingSample
            {
                Timestamp = snapshot.Timestamp,
                CustomerId = customer.CustomerId,
                CustomerName = customer.CustomerName,
                IsShared = customer.IsShared,
                CpuPercent = customer.Totals.CpuPercent,
                MemoryUsedBytes = customer.Totals.MemoryUsedBytes,
                DiskBytes = customer.Totals.DiskBytes,
                ObjectStorageBytes = customer.Totals.ObjectStorageBytes,
                CpuPercentOfCluster = customer.Totals.CpuPercentOfCluster,
                MemoryPercentOfCluster = customer.Totals.MemoryPercentOfCluster,
                BlendedPercentOfCluster = customer.Totals.BlendedPercentOfCluster,
            });
        }

        await _trackContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
