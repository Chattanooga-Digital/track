using Track.Data;
using Track.Services;
using Track.Website.Api.v0.Models.Stats;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Queries;

/// <summary>Joins the latest poll results with customer/stack assignments into the grouped snapshot.</summary>
public class SnapshotBuilder(TrackContext metricsContext, CurrentStateHolder currentStateHolder)
{
    private readonly TrackContext _metricsContext = metricsContext;
    private readonly CurrentStateHolder _currentStateHolder = currentStateHolder;

    public async Task<SnapshotModel> BuildAsync(CancellationToken cancellationToken = default)
    {
        var state = _currentStateHolder.GetCurrent();

        var customers = await _metricsContext.Customers
            .AsNoTracking()
            .Include(c => c.StackAssignments)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var customerIdByStack = customers
            .SelectMany(c => c.StackAssignments.Select(a => (a.StackName, c.Id)))
            .ToDictionary(x => x.StackName, x => x.Id, StringComparer.OrdinalIgnoreCase);

        var stackGroups = state.Samples
            .GroupBy(s => s.StackName)
            .Select(group =>
            {
                var containers = group
                    .OrderBy(s => s.ContainerName)
                    .Select(s => new ContainerStatsModel
                    {
                        NodeName = s.NodeName,
                        ContainerId = s.ContainerId,
                        ContainerName = s.ContainerName,
                        StackName = s.StackName,
                        ServiceName = s.ServiceName,
                        CpuPercent = s.CpuPercent,
                        MemoryUsedBytes = s.MemoryUsedBytes,
                        MemoryLimitBytes = s.MemoryLimitBytes,
                        DiskBytes = state.ContainerLayerBytes.TryGetValue(s.ContainerId, out var disk) ? disk : null,
                        Timestamp = s.Timestamp,
                    })
                    .ToList();

                return new StackGroupModel
                {
                    StackName = group.Key,
                    Containers = containers,
                    Totals = new UsageTotalsModel
                    {
                        CpuPercent = containers.Sum(c => c.CpuPercent),
                        MemoryUsedBytes = containers.Sum(c => c.MemoryUsedBytes),
                        MemoryLimitBytes = containers.Sum(c => c.MemoryLimitBytes),
                        DiskBytes = containers.Sum(c => c.DiskBytes ?? 0)
                            + (group.Key is not null ? state.StackVolumeBytes.GetValueOrDefault(group.Key) : 0),
                    },
                };
            })
            .OrderBy(s => s.StackName is null)
            .ThenBy(s => s.StackName)
            .ToList();

        var snapshot = new SnapshotModel
        {
            Timestamp = DateTime.UtcNow,
            Cluster = new ClusterCapacityModel
            {
                NodeCount = state.ClusterCapacity.NodeCount,
                CpuCores = state.ClusterCapacity.CpuCores,
                MemoryBytes = state.ClusterCapacity.MemoryBytes,
            },
            Nodes = state.Nodes
                .Select(n => new NodeStatusModel { Name = n.Name, Online = n.Online, Error = n.Error, LastSeen = n.LastSeen })
                .ToList(),
        };

        var groupsByCustomerId = new Dictionary<int, CustomerGroupModel>();
        foreach (var customer in customers)
        {
            var group = new CustomerGroupModel { CustomerId = customer.Id, CustomerName = customer.Name };
            groupsByCustomerId[customer.Id] = group;
            snapshot.Customers.Add(group);
        }

        var unassigned = new CustomerGroupModel();
        foreach (var stack in stackGroups)
        {
            var group = stack.StackName is not null && customerIdByStack.TryGetValue(stack.StackName, out var customerId)
                ? groupsByCustomerId[customerId]
                : unassigned;
            group.Stacks.Add(stack);
        }

        if (unassigned.Stacks.Count > 0)
            snapshot.Customers.Add(unassigned);

        foreach (var group in snapshot.Customers)
        {
            group.Totals = new UsageTotalsModel
            {
                CpuPercent = group.Stacks.Sum(s => s.Totals.CpuPercent),
                MemoryUsedBytes = group.Stacks.Sum(s => s.Totals.MemoryUsedBytes),
                MemoryLimitBytes = group.Stacks.Sum(s => s.Totals.MemoryLimitBytes),
                DiskBytes = group.Stacks.Sum(s => s.Totals.DiskBytes),
            };
        }

        // Blended cluster share is computed once capacity is known, at every level of the tree.
        foreach (var stack in stackGroups)
            ApplyClusterShare(stack.Totals, state.ClusterCapacity);
        foreach (var group in snapshot.Customers)
            ApplyClusterShare(group.Totals, state.ClusterCapacity);

        snapshot.Totals = new UsageTotalsModel
        {
            CpuPercent = snapshot.Customers.Sum(c => c.Totals.CpuPercent),
            MemoryUsedBytes = snapshot.Customers.Sum(c => c.Totals.MemoryUsedBytes),
            MemoryLimitBytes = snapshot.Customers.Sum(c => c.Totals.MemoryLimitBytes),
            DiskBytes = snapshot.Customers.Sum(c => c.Totals.DiskBytes),
        };
        ApplyClusterShare(snapshot.Totals, state.ClusterCapacity);

        return snapshot;
    }

    /// <summary>
    /// Fills the cluster-share fields. Container CPU percent is expressed per single core
    /// (100 = one full core), so total CPU capacity in the same unit is cores * 100.
    /// </summary>
    private static void ApplyClusterShare(UsageTotalsModel totals, Track.Services.ClusterCapacity capacity)
    {
        totals.CpuPercentOfCluster = capacity.CpuCores > 0
            ? totals.CpuPercent / (capacity.CpuCores * 100.0) * 100.0
            : 0;
        totals.MemoryPercentOfCluster = capacity.MemoryBytes > 0
            ? (double)totals.MemoryUsedBytes / capacity.MemoryBytes * 100.0
            : 0;
        totals.BlendedPercentOfCluster = (totals.CpuPercentOfCluster + totals.MemoryPercentOfCluster) / 2.0;
    }
}
