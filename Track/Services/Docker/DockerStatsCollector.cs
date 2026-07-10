using global::Docker.DotNet;
using global::Docker.DotNet.Models;
using Track.Configuration;
using Track.Data.Models;
using Microsoft.Extensions.Logging;

namespace Track.Services.Docker;

public sealed class DockerStatsCollector(DockerClientFactory clientFactory, ILogger<DockerStatsCollector> logger)
{
    private const int MaxConcurrentStatsRequests = 8;

    private readonly DockerClientFactory _clientFactory = clientFactory;
    private readonly ILogger<DockerStatsCollector> _logger = logger;

    public async Task<NodeCollection> CollectAsync(
        DockerNodeOptions node, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var client = _clientFactory.GetClient(node.Endpoint);
        var info = await client.System.GetSystemInfoAsync(cancellationToken);
        var capacity = new NodeCapacity(node.Name, info.NCPU, info.MemTotal);
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters(), cancellationToken);

        using var throttle = new SemaphoreSlim(MaxConcurrentStatsRequests);
        var tasks = containers.Select(async container =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var stats = await GetStatsOnceAsync(client, container.ID, cancellationToken);
                if (stats is null)
                    return null;

                return new ContainerSample
                {
                    Timestamp = timestamp,
                    NodeName = node.Name,
                    ContainerId = container.ID,
                    ContainerName = container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID,
                    StackName = DockerLabels.GetStackName(container.Labels),
                    ServiceName = DockerLabels.GetServiceName(container.Labels),
                    CpuPercent = ComputeCpuPercent(stats),
                    MemoryUsedBytes = ComputeMemoryUsedBytes(stats),
                    MemoryLimitBytes = (long)(stats.MemoryStats?.Limit ?? 0),
                };
            }
            catch (DockerApiException ex)
            {
                // The container stopped between listing and the stats call; skip this sample.
                _logger.LogDebug(ex, "Stats unavailable for container {ContainerId} on {Node}", container.ID, node.Name);
                return null;
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        var samples = await Task.WhenAll(tasks);
        return new NodeCollection(capacity, [.. samples.OfType<ContainerSample>()]);
    }

    private static async Task<ContainerStatsResponse?> GetStatsOnceAsync(
        DockerClient client, string containerId, CancellationToken cancellationToken)
    {
        // Stream = false makes the daemon take two readings so PreCPUStats is populated for the CPU delta.
        var progress = new LastValueProgress<ContainerStatsResponse>();
        await client.Containers.GetContainerStatsAsync(
            containerId, new ContainerStatsParameters { Stream = false }, progress, cancellationToken);
        return progress.Value;
    }

    public static double ComputeCpuPercent(ContainerStatsResponse stats)
    {
        if (stats.CPUStats?.CPUUsage is null || stats.PreCPUStats?.CPUUsage is null)
            return 0;

        var cpuDelta = (double)stats.CPUStats.CPUUsage.TotalUsage - stats.PreCPUStats.CPUUsage.TotalUsage;
        var systemDelta = (double)stats.CPUStats.SystemUsage - stats.PreCPUStats.SystemUsage;
        if (cpuDelta <= 0 || systemDelta <= 0)
            return 0;

        var cpuCount = stats.CPUStats.OnlineCPUs > 0
            ? stats.CPUStats.OnlineCPUs
            : (uint)(stats.CPUStats.CPUUsage.PercpuUsage?.Count ?? 1);

        return cpuDelta / systemDelta * cpuCount * 100.0;
    }

    public static long ComputeMemoryUsedBytes(ContainerStatsResponse stats)
    {
        var memory = stats.MemoryStats;
        if (memory is null)
            return 0;

        // Match `docker stats`: exclude page cache (total_inactive_file on cgroup v1, inactive_file on v2).
        var usage = memory.Usage;
        if (memory.Stats is not null)
        {
            if (memory.Stats.TryGetValue("total_inactive_file", out var v1Cache) && v1Cache < usage)
                usage -= v1Cache;
            else if (memory.Stats.TryGetValue("inactive_file", out var v2Cache) && v2Cache < usage)
                usage -= v2Cache;
        }

        return (long)usage;
    }

    private sealed class LastValueProgress<T> : IProgress<T>
    {
        public T? Value { get; private set; }

        public void Report(T value) => Value = value;
    }
}
