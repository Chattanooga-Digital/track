using Track.Configuration;
using Track.Data.Models;
using Track.Enums;

namespace Track.Services.Docker;

public sealed class DockerDiskCollector(DockerDfClient dfClient, ContainerGroupResolver groupResolver)
{
    private readonly DockerDfClient _dfClient = dfClient;
    private readonly ContainerGroupResolver _groupResolver = groupResolver;

    public async Task<List<DiskSample>> CollectAsync(
        DockerNodeOptions node, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var usage = await _dfClient.GetDataUsageAsync(node.Endpoint, cancellationToken);
        var samples = new List<DiskSample>();

        foreach (var container in usage.Containers ?? [])
        {
            // Same grouping key the stats collector produced (env cache shared via the singleton resolver),
            // so a standalone container's writable-layer bytes roll into its synthetic group.
            var group = await _groupResolver.ResolveAsync(
                node.Endpoint, container.Id, container.Labels, cancellationToken);

            samples.Add(new DiskSample
            {
                Timestamp = timestamp,
                NodeName = node.Name,
                Kind = DiskUsageKind.ContainerLayer,
                Name = container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.Id,
                ContainerId = container.Id,
                StackName = group.StackName,
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
