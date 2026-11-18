using Docker.DotNet;
using Docker.DotNet.Models;
using Track.Services.Backups;
using Track.Services.Docker;

namespace Track.Website.Services.Backups;

/// <summary>Agent: runs a stack's backup or restore hook with <c>docker exec</c> in the service's running container.</summary>
public sealed class HookRunner
{
    private static readonly TimeSpan WaitForContainer = TimeSpan.FromMinutes(3);

    /// <summary>The first failure, or null when every hook passed or was skipped.</summary>
    public async Task<string?> RunAllAsync(
        DockerClient client,
        string volumeName,
        IEnumerable<AgentHook> hooks,
        TimeSpan timeout,
        bool requireRunning,
        OutputLog output,
        CancellationToken cancellationToken)
    {
        foreach (var hook in hooks)
        {
            var error = await RunAsync(client, volumeName, hook, timeout, requireRunning, output, cancellationToken);
            if (error is not null)
                return error;
        }

        return null;
    }

    private static async Task<string?> RunAsync(
        DockerClient client,
        string volumeName,
        AgentHook hook,
        TimeSpan timeout,
        bool requireRunning,
        OutputLog output,
        CancellationToken cancellationToken)
    {
        var containerId = await FindContainerAsync(client, volumeName, hook.ServiceName, requireRunning, cancellationToken);
        if (containerId is null)
        {
            if (requireRunning)
                return $"{hook.ServiceName} isn't running, so its hook couldn't run";
            output.Append($"Skipped the {hook.ServiceName} hook: the service isn't running here");
            return null;
        }

        output.Append($"[{hook.ServiceName}] $ {hook.Command}");
        var exec = await client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            Cmd = ["sh", "-c", hook.Command],
            AttachStdout = true,
            AttachStderr = true,
        }, cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var stream = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, timeoutSource.Token);
            await MultiplexedLines.ReadAsync(stream, (line, _) => output.Append(line), timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"The {hook.ServiceName} hook didn't finish within {timeout.TotalMinutes:0} minutes";
        }

        var result = await client.Exec.InspectContainerExecAsync(exec.ID, cancellationToken);
        return result.ExitCode == 0 ? null : $"The {hook.ServiceName} hook exited with code {result.ExitCode}";
    }

    private static async Task<string?> FindContainerAsync(
        DockerClient client, string volumeName, string serviceName, bool wait, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + (wait ? WaitForContainer : TimeSpan.Zero);
        while (true)
        {
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["volume"] = new Dictionary<string, bool> { [volumeName] = true },
                    ["status"] = new Dictionary<string, bool> { ["running"] = true },
                },
            }, cancellationToken);

            var match = containers.FirstOrDefault(c =>
                (c.Labels is null || !c.Labels.ContainsKey(DockerLabels.BackupJob))
                && (DockerLabels.GetServiceName(c.Labels) == serviceName
                    || c.Names?.Any(n => n.TrimStart('/') == serviceName) == true));
            if (match is not null || DateTime.UtcNow >= deadline)
                return match?.ID;

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }
}
