using global::Docker.DotNet.Models;
using Track.Configuration;
using Track.Services.Backups;
using Track.Services.Ingest;

namespace Track.Services.Docker;

/// <summary>Reads services, running tasks and nodes from a manager's engine. Returns null on a worker or standalone engine.</summary>
public sealed class SwarmInventoryCollector(DockerClientFactory clientFactory)
{
    private readonly DockerClientFactory _clientFactory = clientFactory;

    public async Task<SwarmReport?> CollectAsync(string nodeName, CancellationToken cancellationToken)
    {
        var client = _clientFactory.GetClient(TrackOptions.LocalDockerSocket);
        var info = await client.System.GetSystemInfoAsync(cancellationToken);
        if (DockerStatsCollector.Role(info.Swarm) != Enums.NodeRole.Manager)
            return null;

        var nodes = await client.Swarm.ListNodesAsync(cancellationToken);
        var hostnameById = nodes.ToDictionary(n => n.ID, n => n.Description?.Hostname ?? n.ID);
        var services = await client.Swarm.ListServicesAsync(new ServicesListParameters(), cancellationToken);
        var tasks = await client.Tasks.ListAsync(new TasksListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>> { ["desired-state"] = new Dictionary<string, bool> { ["running"] = true } },
        }, cancellationToken);

        var runningByService = tasks
            .Where(t => t.Status?.State == TaskState.Running)
            .GroupBy(t => t.ServiceID)
            .ToDictionary(g => g.Key, g => g
                .Select(t => new SwarmTaskInfo(hostnameById.GetValueOrDefault(t.NodeID) ?? t.NodeID, t.Status?.ContainerStatus?.ContainerID))
                .ToList());

        return new SwarmReport(
            nodeName,
            DateTime.UtcNow,
            nodes.Select(n => new SwarmNodeInfo(
                    n.Description?.Hostname ?? n.ID,
                    n.Spec?.Role ?? "",
                    n.Spec?.Availability ?? "",
                    n.Status?.State ?? "",
                    new Dictionary<string, string>(n.Spec?.Labels ?? new Dictionary<string, string>())))
                .ToList(),
            services.Select(s => Service(s, runningByService.GetValueOrDefault(s.ID) ?? [])).ToList());
    }

    private static SwarmServiceInfo Service(SwarmService service, List<SwarmTaskInfo> running)
    {
        var container = service.Spec.TaskTemplate?.ContainerSpec;
        var labels = BackupLabels.Select(container?.Labels);
        foreach (var (key, value) in BackupLabels.Select(service.Spec.Labels))
            labels[key] = value;

        return new SwarmServiceInfo(
            service.Spec.Name,
            DockerLabels.GetStackName(service.Spec.Labels),
            container?.Image,
            service.Spec.Mode?.Global is not null,
            service.Spec.Mode?.Replicated?.Replicas,
            (container?.Mounts ?? []).Where(m => m.Type == "volume" && !string.IsNullOrEmpty(m.Source)).Select(m => m.Source).ToList(),
            [.. service.Spec.TaskTemplate?.Placement?.Constraints ?? []],
            labels,
            running);
    }
}
