using Docker.DotNet.Models;
using Track.Services.Docker;

namespace Track.Tests;

public class DockerStatsCollectorTests
{
    [Test]
    public void ComputeCpuPercent_UsesDeltasAndCpuCount()
    {
        var stats = new ContainerStatsResponse
        {
            CPUStats = new CPUStats
            {
                CPUUsage = new CPUUsage { TotalUsage = 200_000_000 },
                SystemUsage = 2_000_000_000,
                OnlineCPUs = 4,
            },
            PreCPUStats = new CPUStats
            {
                CPUUsage = new CPUUsage { TotalUsage = 100_000_000 },
                SystemUsage = 1_000_000_000,
            },
        };

        // (100M / 1000M) * 4 cpus * 100 = 40%
        Assert.That(DockerStatsCollector.ComputeCpuPercent(stats), Is.EqualTo(40.0).Within(0.001));
    }

    [Test]
    public void ComputeCpuPercent_ReturnsZeroWithoutDeltas()
    {
        var stats = new ContainerStatsResponse
        {
            CPUStats = new CPUStats { CPUUsage = new CPUUsage { TotalUsage = 100 }, SystemUsage = 1000 },
            PreCPUStats = new CPUStats { CPUUsage = new CPUUsage { TotalUsage = 100 }, SystemUsage = 1000 },
        };

        Assert.That(DockerStatsCollector.ComputeCpuPercent(stats), Is.EqualTo(0));
    }

    [Test]
    public void ComputeMemoryUsedBytes_SubtractsInactiveFileCache()
    {
        var stats = new ContainerStatsResponse
        {
            MemoryStats = new MemoryStats
            {
                Usage = 500_000_000,
                Stats = new Dictionary<string, ulong> { ["inactive_file"] = 100_000_000 },
            },
        };

        Assert.That(DockerStatsCollector.ComputeMemoryUsedBytes(stats), Is.EqualTo(400_000_000));
    }

    [Test]
    public void ComputeMemoryUsedBytes_PrefersCgroupV1TotalInactiveFile()
    {
        var stats = new ContainerStatsResponse
        {
            MemoryStats = new MemoryStats
            {
                Usage = 500_000_000,
                Stats = new Dictionary<string, ulong>
                {
                    ["total_inactive_file"] = 200_000_000,
                    ["inactive_file"] = 100_000_000,
                },
            },
        };

        Assert.That(DockerStatsCollector.ComputeMemoryUsedBytes(stats), Is.EqualTo(300_000_000));
    }
}
