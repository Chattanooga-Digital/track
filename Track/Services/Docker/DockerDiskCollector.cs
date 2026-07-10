using Track.Configuration;
using Track.Data.Models;
using Track.Enums;

namespace Track.Services.Docker;

public sealed class DockerDiskCollector(DockerDfClient dfClient)
{
    private readonly DockerDfClient _dfClient = dfClient;

    public async Task<List<DiskSample>> CollectAsync(
        DockerNodeOptions node, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var usage = await _dfClient.GetDataUsageAsync(node.Endpoint, cancellationToken);
        var samples = new List<DiskSample>();

        foreach (var container in usage.Containers ?? [])
        {
            samples.Add(new DiskSample
            {
                Timestamp = timestamp,
                NodeName = node.Name,
                Kind = DiskUsageKind.ContainerLayer,
                Name = container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.Id,
                ContainerId = container.Id,
                StackName = DockerLabels.GetStackName(container.Labels),
                SizeBytes = container.SizeRw,
            });
        }

        foreach (var volume in usage.Volumes ?? [])
        {
            if (volume.UsageData is null || volume.UsageData.Size < 0)
                continue;

            samples.Add(new DiskSample
            {
                Timestamp = timestamp,
                NodeName = node.Name,
                Kind = DiskUsageKind.Volume,
                Name = volume.Name,
                StackName = DockerLabels.GetStackName(volume.Labels),
                SizeBytes = volume.UsageData.Size,
            });
        }

        return samples;
    }
}
