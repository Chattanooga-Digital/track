using System.Collections.Concurrent;
using System.Globalization;
using Docker.DotNet;
using Docker.DotNet.Models;
using Track.Configuration;
using Track.Enums;
using Track.Services.Backups;
using Track.Services.Docker;
using Microsoft.Extensions.Options;
using RoushTech.Asio;

namespace Track.Website.Services.Backups;

/// <summary>Agent: runs this node's jobs. restic runs in a helper labelled with the job, so a restarted agent adopts it.</summary>
public sealed partial class BackupJobRunner(
    ILogger<BackupJobRunner> logger,
    IOptions<TrackOptions> options,
    DockerClientFactory clientFactory,
    CentralClient centralClient,
    HookRunner hookRunner)
{
    private const string StoppedContainersLabel = "digital.chattanooga.track.backup.stopped";
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScaleTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger<BackupJobRunner> _logger = logger;
    private readonly BackupOptions _options = options.Value.Backups;
    private readonly DockerClientFactory _clientFactory = clientFactory;
    private readonly CentralClient _centralClient = centralClient;
    private readonly HookRunner _hookRunner = hookRunner;
    private readonly ConcurrentDictionary<long, byte> _running = new();

    private string? _helperImage;

    private DockerClient Client => _clientFactory.GetClient(TrackOptions.LocalDockerSocket);

    public void Accept(IEnumerable<long> jobIds, CancellationToken stoppingToken)
    {
        foreach (var id in jobIds)
        {
            if (_running.TryAdd(id, 0))
                _ = Task.Run(() => RunAsync(id, stoppingToken), CancellationToken.None);
        }
    }

    private async Task RunAsync(long id, CancellationToken stoppingToken)
    {
        try
        {
            var job = await _centralClient.GetJobAsync(id, stoppingToken);
            if (job is null)
                return;

            if (job.LogSessionId is { } session)
                JobSessionService.ActivateSession(session);
            _logger.LogInformation("Running {Kind} job {Id} for {Volume}", job.Kind, id, job.VolumeName);
            var output = new OutputLog(_logger);
            AgentJobResult result;
            try
            {
                result = job.Kind switch
                {
                    BackupJobKind.Backup => await BackupAsync(job, output, stoppingToken),
                    BackupJobKind.Restore => await RestoreAsync(job, output, stoppingToken),
                    BackupJobKind.Hook => await HooksAsync(job, output, stoppingToken),
                    BackupJobKind.Scale => await ScaleAsync(job, output, stoppingToken),
                    BackupJobKind.Import => await ImportAsync(job, output, stoppingToken),
                    BackupJobKind.Verify => await VerifyAsync(job, output, stoppingToken),
                    _ => Failed($"Agents don't run {job.Kind} jobs", output),
                };
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Backup job {Id} failed", id);
                result = Failed(ex is DockerApiException docker ? DockerMessage(docker) : ex.Message, output);
            }

            await ReportAsync(id, result, stoppingToken);
            await RemoveHelperAsync(id);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: a running helper keeps going and the next agent picks it up.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup job {Id} could not be run", id);
        }
        finally
        {
            _running.TryRemove(id, out _);
        }
    }

    private async Task<AgentJobResult> BackupAsync(AgentJob job, OutputLog output, CancellationToken cancellationToken)
    {
        if (job.Dump is not null)
            return await DumpBackupAsync(job, output, cancellationToken);

        var client = Client;
        var volume = job.VolumeName!;
        var hookTimeout = TimeSpan.FromSeconds(job.HookTimeoutSeconds);
        var helper = await FindHelperAsync(client, job.Id, cancellationToken);
        var preHooksRan = helper is not null;
        AgentJobResult? result = null;

        if (helper is null)
        {
            if (!await VolumeExistsAsync(client, volume, cancellationToken))
                return Failed($"Volume {volume} isn't on this node", output);

            preHooksRan = true;
            var hookError = await _hookRunner.RunAllAsync(client, volume, job.PreHooks, hookTimeout, false, output, cancellationToken);
            if (hookError is not null)
            {
                result = Failed(hookError, output);
            }
            else
            {
                output.Append(job.Paths.Count == 0 ? $"Backing up {volume}" : $"Backing up {string.Join(", ", job.Paths)} in {volume}");
                helper = await CreateHelperAsync(client, job, BackupArguments(job), [$"{volume}:/data/{volume}:ro"], null, cancellationToken);
            }
        }

        if (helper is not null)
        {
            var outcome = await WatchHelperAsync(client, job.Id, helper, output, cancellationToken);
            result = outcome switch
            {
                { Cancelled: true } => Cancelled(output),
                { ExitCode: 0 } => Succeeded(outcome.Summary, output),
                { ExitCode: 3 } => Failed("Some files couldn't be read, so the snapshot is incomplete", output, outcome.Summary),
                _ => Failed(ResticError(outcome.ExitCode, output), output),
            };
        }

        if (preHooksRan)
        {
            var postError = await _hookRunner.RunAllAsync(client, volume, job.PostHooks, hookTimeout, false, output, cancellationToken);
            if (postError is not null && result!.Succeeded)
                result = result with { Succeeded = false, Error = postError };
        }

        return result!;
    }

    private async Task<AgentJobResult> RestoreAsync(AgentJob job, OutputLog output, CancellationToken cancellationToken)
    {
        var client = Client;
        var target = job.RestoreVolume!;
        var helper = await FindHelperAsync(client, job.Id, cancellationToken);
        List<string> stopped;

        if (helper is null)
        {
            if (target == job.VolumeName)
            {
                if (!await VolumeExistsAsync(client, target, cancellationToken))
                    return Failed($"Volume {target} isn't on this node", output);
            }
            else
            {
                await client.Volumes.CreateAsync(new VolumesCreateParameters
                {
                    Name = target,
                    Labels = new Dictionary<string, string> { [DockerLabels.RestoreOf] = job.VolumeName! },
                }, cancellationToken);
                output.Append($"Created volume {target}");
            }

            stopped = job.StopContainers ? await StopUsersAsync(client, target, output, cancellationToken) : [];
            output.Append($"Restoring snapshot {job.SnapshotId?[..Math.Min(8, job.SnapshotId.Length)]} into {target}");
            var arguments = new List<string> { "restore", "--json", $"{job.SnapshotId}:{job.SnapshotPath}", "--target", "/restore" };
            if (job.RestoreDelete)
                arguments.Add("--delete");
            arguments.AddRange(job.Restic!.Options);
            helper = await CreateHelperAsync(client, job, arguments, [$"{target}:/restore"], stopped, cancellationToken);
        }
        else
        {
            var inspect = await client.Containers.InspectContainerAsync(helper, cancellationToken);
            stopped = inspect.Config.Labels is { } labels && labels.TryGetValue(StoppedContainersLabel, out var ids)
                ? [.. ids.Split(',', StringSplitOptions.RemoveEmptyEntries)]
                : [];
        }

        HelperOutcome outcome;
        try
        {
            outcome = await WatchHelperAsync(client, job.Id, helper, output, cancellationToken);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                await StartAsync(client, stopped, output);
        }

        return outcome switch
        {
            { Cancelled: true } => Cancelled(output),
            { ExitCode: 0 } => Succeeded(outcome.Summary, output),
            _ => Failed(ResticError(outcome.ExitCode, output), output),
        };
    }

    private async Task<AgentJobResult> HooksAsync(AgentJob job, OutputLog output, CancellationToken cancellationToken)
    {
        var error = await _hookRunner.RunAllAsync(
            Client, job.VolumeName!, job.Hooks, TimeSpan.FromSeconds(job.HookTimeoutSeconds), true, output, cancellationToken);
        return error is null ? Succeeded(null, output) : Failed(error, output);
    }

    private async Task<AgentJobResult> ScaleAsync(AgentJob job, OutputLog output, CancellationToken cancellationToken)
    {
        var client = Client;
        foreach (var scale in job.Scale)
        {
            var service = await client.Swarm.InspectServiceAsync(scale.ServiceName, cancellationToken);
            if (service.Spec.Mode?.Replicated is null)
                return Failed($"{scale.ServiceName} isn't a replicated service, so it can't be scaled", output);

            service.Spec.Mode.Replicated.Replicas = scale.Replicas;
            await client.Swarm.UpdateServiceAsync(service.ID, new ServiceUpdateParameters
            {
                Service = service.Spec,
                Version = (long)service.Version.Index,
            }, cancellationToken);
            output.Append($"Scaled {scale.ServiceName} to {scale.Replicas}");
        }

        var deadline = DateTime.UtcNow + ScaleTimeout;
        foreach (var scale in job.Scale)
        {
            while (true)
            {
                var tasks = await client.Tasks.ListAsync(new TasksListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>> { ["service"] = new Dictionary<string, bool> { [scale.ServiceName] = true } },
                }, cancellationToken);
                var running = tasks.Count(t => t.Status?.State == TaskState.Running && t.DesiredState == TaskState.Running);
                var lingering = tasks.Count(t => t.Status?.State == TaskState.Running);
                if (scale.Replicas == 0 ? lingering == 0 : running >= (int)scale.Replicas)
                    break;
                if (DateTime.UtcNow >= deadline)
                    return Failed($"{scale.ServiceName} didn't reach {scale.Replicas} running within {ScaleTimeout.TotalMinutes:0} minutes", output);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            output.Append($"{scale.ServiceName} has {scale.Replicas} running");
        }

        return Succeeded(null, output);
    }

    private async Task<HelperOutcome> WatchHelperAsync(
        DockerClient client, long jobId, string containerId, OutputLog output, CancellationToken cancellationToken)
    {
        var inspect = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
        if (inspect.State?.Status == "created")
            await client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);

        var state = new HelperState();
        using var progressSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progress = ReportProgressAsync(client, jobId, containerId, state, progressSource.Token);
        try
        {
            using (var stream = await client.Containers.GetContainerLogsAsync(containerId, false, new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
            }, cancellationToken))
            {
                await MultiplexedLines.ReadAsync(stream, (line, isError) =>
                {
                    switch (isError ? null : ResticJson.ParseLine(line))
                    {
                        case ResticStatus status:
                            state.Status = status;
                            break;
                        case ResticSummary summary:
                            state.Summary = summary;
                            break;
                        default:
                            output.Append(line);
                            break;
                    }
                }, cancellationToken);
            }

            var exit = await client.Containers.WaitContainerAsync(containerId, cancellationToken);
            return new HelperOutcome(exit.StatusCode, state.Summary, state.Cancelled);
        }
        finally
        {
            await progressSource.CancelAsync();
            try
            {
                await progress;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task ReportProgressAsync(
        DockerClient client, long jobId, string containerId, HelperState state, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ProgressInterval);
        long? lastLogged = null;
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var status = state.Status;
                if (status?.BytesDone is { } done && done != lastLogged)
                {
                    lastLogged = done;
                    _logger.LogInformation("{Progress}", Describe(status));
                }

                var response = await _centralClient.PostProgressAsync(
                    jobId, new AgentJobProgress(status?.PercentDone, status?.BytesDone, status?.TotalBytes), cancellationToken);
                if (response?.Cancel == true && !state.Cancelled)
                {
                    state.Cancelled = true;
                    await client.Containers.KillContainerAsync(containerId, new ContainerKillParameters(), cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Progress for backup job {Id} wasn't delivered", jobId);
            }
        }
    }

    private async Task<string> CreateHelperAsync(
        DockerClient client,
        AgentJob job,
        List<string> arguments,
        List<string> binds,
        List<string>? stoppedContainers,
        CancellationToken cancellationToken,
        bool openStdin = false,
        bool attachOutput = false)
    {
        var labels = new Dictionary<string, string> { [DockerLabels.BackupJob] = job.Id.ToString(CultureInfo.InvariantCulture) };
        if (stoppedContainers is { Count: > 0 })
            labels[StoppedContainersLabel] = string.Join(',', stoppedContainers);

        var environment = job.Restic!.Environment.Select(e => $"{e.Key}={e.Value}").ToList();
        environment.Add("RESTIC_CACHE_DIR=/cache");
        environment.Add("RESTIC_PROGRESS_FPS=0.2");

        // Root, because volumes hold files owned by whatever user each app runs as, and restores must put owners back.
        var created = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = $"track-backup-{job.Id}",
            Image = await HelperImageAsync(client, cancellationToken),
            Entrypoint = ["restic"],
            Cmd = arguments,
            Env = environment,
            User = "0:0",
            Labels = labels,
            OpenStdin = openStdin,
            StdinOnce = openStdin,
            AttachStdin = openStdin,
            AttachStdout = attachOutput,
            AttachStderr = attachOutput,
            HostConfig = new HostConfig
            {
                Binds = [.. binds, $"{_options.CacheVolume}:/cache"],
                NetworkMode = _options.HelperNetwork,
                // A dump read back out of restic is the whole database; it must not end up in the container log.
                LogConfig = attachOutput ? new LogConfig { Type = "none" } : null,
            },
        }, cancellationToken);
        return created.ID;
    }

    private async Task<string> HelperImageAsync(DockerClient client, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.HelperImage))
            return _options.HelperImage;

        return _helperImage ??= (await AgentSelfUpdater.FindSelfAsync(client, cancellationToken))?.Image
            ?? throw new InvalidOperationException(
                "Can't find this agent's own image to run restic from; set Track__Backups__HelperImage");
    }

    private static List<string> BackupArguments(AgentJob job)
    {
        var arguments = new List<string> { "backup", "--json", "--host", job.Host! };
        foreach (var tag in job.Tags)
        {
            arguments.Add("--tag");
            arguments.Add(tag);
        }

        arguments.AddRange(job.Restic!.Options);
        arguments.AddRange(job.Paths.Count == 0
            ? [$"/data/{job.VolumeName}"]
            : job.Paths.Select(path => $"/data/{job.VolumeName}/{path}"));
        return arguments;
    }

    private static async Task<string?> FindHelperAsync(DockerClient client, long jobId, CancellationToken cancellationToken)
    {
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [$"{DockerLabels.BackupJob}={jobId}"] = true },
            },
        }, cancellationToken);
        return containers.FirstOrDefault()?.ID;
    }

    private async Task RemoveHelperAsync(long jobId)
    {
        try
        {
            var client = Client;
            if (await FindHelperAsync(client, jobId, CancellationToken.None) is { } helper)
                await client.Containers.RemoveContainerAsync(helper, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
            if (await FindScratchAsync(client, jobId, CancellationToken.None) is { } scratch)
                await client.Containers.RemoveContainerAsync(scratch, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't remove the helper container for backup job {Id}", jobId);
        }
    }

    private static async Task<bool> VolumeExistsAsync(DockerClient client, string volume, CancellationToken cancellationToken)
    {
        try
        {
            await client.Volumes.InspectAsync(volume, cancellationToken);
            return true;
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static async Task<List<string>> StopUsersAsync(DockerClient client, string volume, OutputLog output, CancellationToken cancellationToken)
    {
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["volume"] = new Dictionary<string, bool> { [volume] = true },
                ["status"] = new Dictionary<string, bool> { ["running"] = true },
            },
        }, cancellationToken);

        var stopped = new List<string>();
        foreach (var container in containers.Where(c => c.Labels is null || !c.Labels.ContainsKey(DockerLabels.BackupJob)))
        {
            await client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters(), cancellationToken);
            stopped.Add(container.ID);
            output.Append($"Stopped {container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID}");
        }

        return stopped;
    }

    private async Task StartAsync(DockerClient client, List<string> containerIds, OutputLog output)
    {
        foreach (var id in containerIds)
        {
            try
            {
                await client.Containers.StartContainerAsync(id, new ContainerStartParameters(), CancellationToken.None);
                output.Append($"Started {id[..Math.Min(12, id.Length)]}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Couldn't restart container {Id} after a restore", id);
                output.Append($"Couldn't start {id[..Math.Min(12, id.Length)]}: {ex.Message}");
            }
        }
    }

    private async Task ReportAsync(long id, AgentJobResult result, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _centralClient.PostResultAsync(id, result, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 6)
            {
                _logger.LogWarning(ex, "Couldn't report backup job {Id}; retrying", id);
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
    }

    private static string Describe(ResticStatus status)
    {
        var done = Size(status.BytesDone ?? 0);
        var amount = status.TotalBytes is > 0 ? $"{done} of {Size(status.TotalBytes.Value)}" : done;
        return status.PercentDone is { } percent ? $"{percent:P0} · {amount}" : amount;
    }

    private static string ResticError(long exitCode, OutputLog output) => exitCode switch
    {
        10 => "The repository doesn't exist",
        11 => "The repository is locked by another operation",
        12 => "The repository password is wrong",
        _ => output.LastLine is { } line ? $"restic exited with code {exitCode}: {line}" : $"restic exited with code {exitCode}",
    };

    private static string DockerMessage(DockerApiException ex) =>
        string.IsNullOrWhiteSpace(ex.ResponseBody) ? ex.Message : ex.ResponseBody.Trim();

    private static AgentJobResult Succeeded(ResticSummary? summary, OutputLog output) =>
        new(true, false, null, summary?.SnapshotId, summary?.BytesAdded, summary?.FilesNew, summary?.FilesChanged,
            summary?.TotalBytes, output.ToString());

    private static AgentJobResult Failed(string error, OutputLog output, ResticSummary? summary = null) =>
        new(false, false, error, summary?.SnapshotId, summary?.BytesAdded, summary?.FilesNew, summary?.FilesChanged,
            summary?.TotalBytes, output.ToString());

    private static AgentJobResult Cancelled(OutputLog output) =>
        new(false, true, "Cancelled", null, null, null, null, null, output.ToString());

    private sealed record HelperOutcome(long ExitCode, ResticSummary? Summary, bool Cancelled);

    private sealed class HelperState
    {
        private volatile ResticStatus? _status;
        private volatile ResticSummary? _summary;
        private volatile bool _cancelled;

        public ResticStatus? Status { get => _status; set => _status = value; }
        public ResticSummary? Summary { get => _summary; set => _summary = value; }
        public bool Cancelled { get => _cancelled; set => _cancelled = value; }
    }
}
