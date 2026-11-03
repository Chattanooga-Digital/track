using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Services.Billing;
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
            .Include(c => c.BucketAssignments)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var customerIdByStack = customers
            .SelectMany(c => c.StackAssignments.Select(a => (a.StackName, c.Id)))
            .ToDictionary(x => x.StackName, x => x.Id, StringComparer.OrdinalIgnoreCase);

        var sharedStackNames = (await _metricsContext.SharedStacks
                .AsNoTracking()
                .Select(s => s.StackName)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sharedBucketKeys = (await _metricsContext.SharedBuckets
                .AsNoTracking()
                .Select(b => new { b.SourceName, b.BucketName })
                .ToListAsync(cancellationToken))
            .Select(b => BucketKey(b.SourceName, b.BucketName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var customerIdByBucket = customers
            .SelectMany(c => c.BucketAssignments.Select(a => (Key: BucketKey(a.SourceName, a.BucketName), c.Id)))
            .ToDictionary(x => x.Key, x => x.Id, StringComparer.OrdinalIgnoreCase);

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
                .Select(n =>
                {
                    var nodeSamples = state.Samples.Where(s => s.NodeName == n.Name).ToList();
                    var capacity = state.CapacityByNode.GetValueOrDefault(n.Name);
                    return new NodeStatusModel
                    {
                        Name = n.Name,
                        Online = n.Online,
                        Error = n.Error,
                        LastSeen = n.LastSeen,
                        ContainersTracked = nodeSamples.Count,
                        UnidentifiedContainers = nodeSamples.Count(s => s.StackName is null),
                        CpuCores = capacity?.CpuCores ?? 0,
                        MemoryBytes = capacity?.MemoryBytes ?? 0,
                        CpuPercent = nodeSamples.Sum(s => s.CpuPercent),
                        MemoryUsedBytes = nodeSamples.Sum(s => s.MemoryUsedBytes),
                    };
                })
                .ToList(),
        };

        var groupsByCustomerId = new Dictionary<int, CustomerGroupModel>();
        foreach (var customer in customers)
        {
            var group = new CustomerGroupModel { CustomerId = customer.Id, CustomerName = customer.Name };
            groupsByCustomerId[customer.Id] = group;
            snapshot.Customers.Add(group);
        }

        var shared = new CustomerGroupModel { IsShared = true };
        var unassigned = new CustomerGroupModel();
        foreach (var stack in stackGroups)
        {
            var group = stack.StackName switch
            {
                not null when customerIdByStack.TryGetValue(stack.StackName, out var customerId) => groupsByCustomerId[customerId],
                not null when sharedStackNames.Contains(stack.StackName) => shared,
                _ => unassigned,
            };
            group.Stacks.Add(stack);
        }

        foreach (var bucket in state.Buckets)
        {
            var key = BucketKey(bucket.SourceName, bucket.BucketName);
            var group = customerIdByBucket.TryGetValue(key, out var customerId)
                ? groupsByCustomerId[customerId]
                : sharedBucketKeys.Contains(key) ? shared : unassigned;
            group.Buckets.Add(new BucketUsageModel
            {
                SourceName = bucket.SourceName,
                BucketName = bucket.BucketName,
                SizeBytes = bucket.SizeBytes,
                ObjectCount = bucket.ObjectCount,
            });
        }

        if (shared.Stacks.Count > 0 || shared.Buckets.Count > 0)
            snapshot.Customers.Add(shared);
        if (unassigned.Stacks.Count > 0 || unassigned.Buckets.Count > 0)
            snapshot.Customers.Add(unassigned);

        foreach (var group in snapshot.Customers)
        {
            group.Totals = new UsageTotalsModel
            {
                CpuPercent = group.Stacks.Sum(s => s.Totals.CpuPercent),
                MemoryUsedBytes = group.Stacks.Sum(s => s.Totals.MemoryUsedBytes),
                MemoryLimitBytes = group.Stacks.Sum(s => s.Totals.MemoryLimitBytes),
                DiskBytes = group.Stacks.Sum(s => s.Totals.DiskBytes),
                ObjectStorageBytes = group.Buckets.Sum(b => b.SizeBytes),
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
            ObjectStorageBytes = snapshot.Customers.Sum(c => c.Totals.ObjectStorageBytes),
        };
        ApplyClusterShare(snapshot.Totals, state.ClusterCapacity);

        await ApplyCostsAsync(snapshot, cancellationToken);

        return snapshot;
    }

    /// <summary>
    /// Turns the blended shares into dollars. Compute is full cost recovery: the fixed cluster cost is spread
    /// across paying customers by blended footprint (shared and unassigned excluded from the denominator), so customer
    /// bills sum to the cluster cost. Storage is a flat per-GB markup on assigned usage.
    /// </summary>
    private async Task ApplyCostsAsync(SnapshotModel snapshot, CancellationToken cancellationToken)
    {
        var rate = await _metricsContext.BillingRates.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken) ?? new BillingRate();
        snapshot.Currency = rate.Currency;

        var totalCustomerBlended = snapshot.Customers
            .Where(c => c.CustomerId is not null)
            .Sum(c => c.Totals.BlendedPercentOfCluster);
        var baseRate = BillingCostCalculator.ComputeBaseRate(rate.ClusterComputeMonthlyCost, totalCustomerBlended);

        foreach (var group in snapshot.Customers)
        {
            var assigned = group.CustomerId is not null;
            foreach (var stack in group.Stacks)
                ApplyCost(stack.Totals, baseRate, assigned, rate);
            ApplyCost(group.Totals, baseRate, assigned, rate);
        }

        // Cluster total is the sum of what customers actually pay, not the formula over the grand blended
        // (which would include the unassigned remainder and overshoot the recovered cost).
        snapshot.Totals.ComputeCost = snapshot.Customers.Sum(c => c.Totals.ComputeCost);
        snapshot.Totals.DiskCost = snapshot.Customers.Sum(c => c.Totals.DiskCost);
        snapshot.Totals.ObjectStorageCost = snapshot.Customers.Sum(c => c.Totals.ObjectStorageCost);
        snapshot.Totals.TotalCost = snapshot.Totals.ComputeCost + snapshot.Totals.DiskCost + snapshot.Totals.ObjectStorageCost;
    }

    private static void ApplyCost(UsageTotalsModel totals, decimal baseRate, bool assigned, BillingRate rate)
    {
        var costs = BillingCostCalculator.ForScope(
            assigned ? totals.BlendedPercentOfCluster : 0,
            baseRate,
            assigned ? totals.DiskBytes : 0,
            assigned ? totals.ObjectStorageBytes : 0,
            rate);
        totals.ComputeCost = costs.ComputeCost;
        totals.DiskCost = costs.DiskCost;
        totals.ObjectStorageCost = costs.ObjectStorageCost;
        totals.TotalCost = costs.TotalCost;
    }

    /// <summary>Stable composite key for a bucket's (source, name) identity; matched case-insensitively.</summary>
    private static string BucketKey(string sourceName, string bucketName) => $"{sourceName}\u001f{bucketName}";

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
