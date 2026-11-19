using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Track.Enums;
using Track.Services.Backups;
using Track.Services.Docker;

namespace Track.Website.Services.Backups;

public sealed partial class BackupJobRunner
{
    private const string DumpCompleteMarker = "/tmp/track-dump-complete";
    private const long ScratchMemoryBytes = 1024L * 1024 * 1024;
    private static readonly TimeSpan ScratchReadyTimeout = TimeSpan.FromMinutes(5);

    private async Task<AgentJobResult> DumpBackupAsync(AgentJob job, OutputLog output, CancellationToken cancellationToken)
    {
        var client = Client;
        var dump = job.Dump!;

        if (await FindHelperAsync(client, job.Id, cancellationToken) is { } adopted)
        {
            // The dump stream ended with the previous agent; restic only saves the snapshot if the marker was written first.
            var outcome = await WatchHelperAsync(client, job.Id, adopted, output, cancellationToken);
            return outcome.ExitCode == 0 && outcome.Summary?.SnapshotId is not null
                ? Succeeded(outcome.Summary, output)
                : Failed("The agent restarted during the dump", output);
        }

        var database = await HookRunner.FindContainerAsync(client, job.VolumeName!, dump.ServiceName!, false, cancellationToken);
        if (database is null)
            return Failed($"{dump.ServiceName} isn't running here, so its database can't be dumped", output);

        output.Append($"Dumping {dump.Engine} from {dump.ServiceName}");
        var helper = await CreateHelperAsync(client, job, DumpBackupArguments(job), [], null, cancellationToken, openStdin: true);
        using var stdin = await client.Containers.AttachContainerAsync(
            helper, false, new ContainerAttachParameters { Stream = true, Stdin = true }, cancellationToken);
        await client.Containers.StartContainerAsync(helper, new ContainerStartParameters(), cancellationToken);
        var watch = WatchHelperAsync(client, job.Id, helper, output, cancellationToken);

        PipeResult dumped;
        string? markerError = null;
        try
        {
            dumped = await PipeExecAsync(client, database, DatabaseDumps.DumpCommand(dump.Engine), stdin, output, null, cancellationToken);
            if (dumped is { ExitCode: 0, SinkFailed: false })
            {
                output.Append($"Dumped {Size(dumped.Bytes)}");
                markerError = await MarkDumpCompleteAsync(client, helper, cancellationToken);
            }
        }
        finally
        {
            stdin.CloseWrite();
        }

        var restic = await watch;
        if (restic.Cancelled)
            return Cancelled(output);
        if (dumped.SinkFailed)
            return Failed(ResticError(restic.ExitCode, output), output);
        if (dumped.ExitCode != 0)
            return Failed($"The dump failed: {dumped.LastError ?? $"exit code {dumped.ExitCode}"}", output);
        if (markerError is not null)
            return Failed(markerError, output);
        return restic.ExitCode == 0 ? Succeeded(restic.Summary, output) : Failed(ResticError(restic.ExitCode, output), output);
    }

    private async Task<AgentJobResult> ImportAsync(AgentJob job, OutputLog output, CancellationToken cancellationToken)
    {
        var client = Client;
        var dump = job.Dump!;
        if (await FindHelperAsync(client, job.Id, cancellationToken) is not null)
            return Failed("The agent restarted during the import, so the database may be partly restored", output);

        var database = await HookRunner.FindContainerAsync(client, job.VolumeName!, dump.ServiceName!, true, cancellationToken);
        if (database is null)
            return Failed($"{dump.ServiceName} isn't running here", output);

        var stopped = job.StopContainers ? await StopStackAsync(client, database, output, cancellationToken) : [];
        ImportOutcome import;
        try
        {
            import = await ImportSnapshotAsync(client, job, database, dump.Engine, output, cancellationToken);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                await StartAsync(client, stopped, output);
        }

        if (import.Cancelled)
            return Cancelled(output);
        return import.Error is null
            ? Succeeded(null, output) with { Summary = $"Imported {Size(import.Bytes)}" }
            : Failed(import.Error, output);
    }

    private async Task<AgentJobResult> VerifyAsync(AgentJob job, OutputLog output, CancellationToken cancellationToken)
    {
        var client = Client;
        var dump = job.Dump!;
        await RemoveHelperAsync(job.Id);
        await EnsureImageAsync(client, dump.Image!, output, cancellationToken);

        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var scratch = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = $"track-verify-{job.Id}",
            Image = dump.Image,
            Env = [.. DatabaseDumps.ScratchEnvironment(dump.Engine, password).Select(e => $"{e.Key}={e.Value}")],
            Labels = new Dictionary<string, string>
            {
                [DockerLabels.BackupScratch] = job.Id.ToString(CultureInfo.InvariantCulture),
                [DockerLabels.TrackStack] = "track",
            },
            HostConfig = new HostConfig { NetworkMode = "none", Memory = ScratchMemoryBytes },
        }, cancellationToken);

        try
        {
            await client.Containers.StartContainerAsync(scratch.ID, new ContainerStartParameters(), cancellationToken);
            output.Append($"Started a throwaway {dump.Image}, waiting for it to accept connections");
            if (await WaitUntilReadyAsync(client, scratch.ID, dump.Engine, cancellationToken) is { } notReady)
                return Failed(notReady, output);

            var import = await ImportSnapshotAsync(client, job, scratch.ID, dump.Engine, output, cancellationToken);
            if (import.Cancelled)
                return Cancelled(output);
            if (import.Error is not null)
                return Failed(import.Error, output);

            var counted = await ExecAsync(client, scratch.ID, ["sh", "-c", DatabaseDumps.CountCommand(dump.Engine)], cancellationToken);
            var counts = counted.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (counted.ExitCode != 0 || counts.Length < 2)
                return Failed($"Couldn't count what was restored: {counted.Output.Trim()}", output);

            var summary = $"{Size(import.Bytes)} into {Plural(counts[0], "database")} with {Plural(counts[1], "table")}";
            output.Append(summary);
            return Succeeded(null, output) with { Summary = summary };
        }
        finally
        {
            await client.Containers.RemoveContainerAsync(
                scratch.ID, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, CancellationToken.None);
        }
    }

    /// <summary>Streams the job's dump snapshot out of restic and into <paramref name="containerId"/>'s database.</summary>
    private async Task<ImportOutcome> ImportSnapshotAsync(
        DockerClient client, AgentJob job, string containerId, DatabaseEngine engine, OutputLog output, CancellationToken cancellationToken)
    {
        var snapshotId = job.SnapshotId!;
        output.Append($"Importing snapshot {snapshotId[..Math.Min(8, snapshotId.Length)]}");
        var helper = await CreateHelperAsync(client, job,
            ["dump", job.SnapshotId!, job.SnapshotPath!, .. job.Restic!.Options], [], null, cancellationToken, attachOutput: true);
        using var source = await client.Containers.AttachContainerAsync(
            helper, false, new ContainerAttachParameters { Stream = true, Stdout = true, Stderr = true }, cancellationToken);

        var exec = await client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            Cmd = ["sh", "-c", DatabaseDumps.ImportCommand(engine)],
            AttachStdin = true,
            AttachStdout = true,
            AttachStderr = true,
        }, cancellationToken);
        using var sink = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cancellationToken);
        await client.Containers.StartContainerAsync(helper, new ContainerStartParameters(), cancellationToken);

        var state = new HelperState();
        using var progressSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progress = ReportProgressAsync(client, job.Id, helper, state, progressSource.Token);

        var importErrors = new List<string>();
        var importOutput = MultiplexedLines.ReadAsync(sink, (line, _) =>
        {
            output.Append(line);
            if (DatabaseDumps.IsImportError(line) && !DatabaseDumps.IsHarmlessImportError(line))
            {
                lock (importErrors)
                    importErrors.Add(line);
            }
        }, cancellationToken);

        PipeResult piped;
        try
        {
            piped = await PipeAsync(source, sink, output, bytes => state.Status = new ResticStatus(null, bytes, null), cancellationToken);
        }
        finally
        {
            sink.CloseWrite();
            await progressSource.CancelAsync();
            try
            {
                await progress;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await importOutput;
        var restic = await client.Containers.WaitContainerAsync(helper, cancellationToken);
        var importExitCode = await ExecExitCodeAsync(client, exec.ID, cancellationToken);

        if (state.Cancelled)
            return new ImportOutcome(null, piped.Bytes, true);
        if (restic.StatusCode != 0)
            return new ImportOutcome($"restic couldn't read the snapshot: {piped.LastError ?? $"exit code {restic.StatusCode}"}", piped.Bytes, false);
        if (piped.SinkFailed || importExitCode != 0)
            return new ImportOutcome($"The import failed: {importErrors.FirstOrDefault() ?? output.LastLine ?? $"exit code {importExitCode}"}", piped.Bytes, false);
        if (importErrors.Count > 0)
            return new ImportOutcome($"{Plural(importErrors.Count.ToString(CultureInfo.InvariantCulture), "statement")} failed, first: {importErrors[0]}", piped.Bytes, false);
        return new ImportOutcome(null, piped.Bytes, false);
    }

    private static async Task<PipeResult> PipeExecAsync(
        DockerClient client, string containerId, string command, MultiplexedStream sink, OutputLog output, Action<long>? onBytes,
        CancellationToken cancellationToken)
    {
        var exec = await client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            Cmd = ["sh", "-c", command],
            AttachStdout = true,
            AttachStderr = true,
        }, cancellationToken);

        PipeResult piped;
        using (var source = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cancellationToken))
            piped = await PipeAsync(source, sink, output, onBytes, cancellationToken);

        return piped with { ExitCode = await ExecExitCodeAsync(client, exec.ID, cancellationToken) };
    }

    /// <summary>Copies <paramref name="source"/>'s stdout into <paramref name="sink"/>'s stdin and its stderr into the job output.</summary>
    private static async Task<PipeResult> PipeAsync(
        MultiplexedStream source, MultiplexedStream sink, OutputLog output, Action<long>? onBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var errors = new LineBuffer();
        string? lastError = null;
        long bytes = 0;

        while (true)
        {
            var read = await source.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read.EOF)
                break;

            if (read.Target == MultiplexedStream.TargetStream.StandardError)
            {
                foreach (var line in errors.Add(buffer, read.Count))
                {
                    output.Append(line);
                    lastError = line;
                }
                continue;
            }

            try
            {
                await sink.WriteAsync(buffer, 0, read.Count, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                return new PipeResult(0, bytes, true, lastError);
            }

            bytes += read.Count;
            onBytes?.Invoke(bytes);
        }

        foreach (var line in errors.Flush())
        {
            output.Append(line);
            lastError = line;
        }

        return new PipeResult(0, bytes, false, lastError);
    }

    private static async Task<string?> MarkDumpCompleteAsync(DockerClient client, string helper, CancellationToken cancellationToken)
    {
        var marked = await ExecAsync(client, helper, ["touch", DumpCompleteMarker], cancellationToken);
        return marked.ExitCode == 0 ? null : $"Couldn't finish the dump: {marked.Output.Trim()}";
    }

    private static async Task<string?> WaitUntilReadyAsync(
        DockerClient client, string containerId, DatabaseEngine engine, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ScratchReadyTimeout;
        while (true)
        {
            var inspect = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
            if (inspect.State?.Running != true)
                return $"The test database stopped while starting (exit code {inspect.State?.ExitCode})";

            var ready = await ExecAsync(client, containerId, ["sh", "-c", DatabaseDumps.ReadyCommand(engine)], cancellationToken);
            if (ready.ExitCode == 0)
                return null;
            if (DateTime.UtcNow >= deadline)
                return $"The test database didn't start within {ScratchReadyTimeout.TotalMinutes:0} minutes";

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private static async Task<(long ExitCode, string Output)> ExecAsync(
        DockerClient client, string containerId, IList<string> command, CancellationToken cancellationToken)
    {
        var exec = await client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            Cmd = command,
            AttachStdout = true,
            AttachStderr = true,
        }, cancellationToken);

        var text = new StringBuilder();
        using (var stream = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cancellationToken))
            await MultiplexedLines.ReadAsync(stream, (line, _) => text.AppendLine(line), cancellationToken);

        return (await ExecExitCodeAsync(client, exec.ID, cancellationToken), text.ToString());
    }

    // The output can end a moment before the engine records the exit code.
    private static async Task<long> ExecExitCodeAsync(DockerClient client, string execId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var inspect = await client.Exec.InspectContainerExecAsync(execId, cancellationToken);
            if (!inspect.Running)
                return inspect.ExitCode;
            if (attempt >= 50)
                return -1;
            await Task.Delay(200, cancellationToken);
        }
    }

    private static async Task EnsureImageAsync(DockerClient client, string image, OutputLog output, CancellationToken cancellationToken)
    {
        try
        {
            await client.Images.InspectImageAsync(image, cancellationToken);
            return;
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }

        output.Append($"Pulling {image}");
        await client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = image }, null, new Progress<JSONMessage>(), cancellationToken);
    }

    /// <summary>Stops the database's stack-mates outside Swarm, where nothing else would hold them off during the import.</summary>
    private static async Task<List<string>> StopStackAsync(
        DockerClient client, string databaseId, OutputLog output, CancellationToken cancellationToken)
    {
        var database = await client.Containers.InspectContainerAsync(databaseId, cancellationToken);
        var stack = DockerLabels.GetStackName(database.Config?.Labels);
        if (stack is null)
            return [];

        var running = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>> { ["status"] = new Dictionary<string, bool> { ["running"] = true } },
        }, cancellationToken);

        var stopped = new List<string>();
        foreach (var container in running.Where(c => c.ID != database.ID
                     && DockerLabels.GetStackName(c.Labels) == stack
                     && c.Labels?.ContainsKey(DockerLabels.BackupJob) != true
                     && c.Labels?.ContainsKey(DockerLabels.BackupScratch) != true))
        {
            await client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters(), cancellationToken);
            stopped.Add(container.ID);
            output.Append($"Stopped {container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID}");
        }

        return stopped;
    }

    private static async Task<string?> FindScratchAsync(DockerClient client, long jobId, CancellationToken cancellationToken)
    {
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [$"{DockerLabels.BackupScratch}={jobId}"] = true },
            },
        }, cancellationToken);
        return containers.FirstOrDefault()?.ID;
    }

    private static List<string> DumpBackupArguments(AgentJob job)
    {
        var arguments = new List<string> { "backup", "--json", "--host", job.Host! };
        foreach (var tag in job.Tags)
        {
            arguments.Add("--tag");
            arguments.Add(tag);
        }

        arguments.AddRange(job.Restic!.Options);
        // restic reads the agent's stream from its own stdin, and saves nothing unless the agent marked the dump complete.
        arguments.AddRange([
            "--stdin-from-command",
            "--stdin-filename", $"/data/{job.VolumeName}/{DatabaseDumps.FileName}",
            "--", "sh", "-c", $"cat /proc/1/fd/0 && test -e {DumpCompleteMarker}",
        ]);
        return arguments;
    }

    private static string Plural(string count, string noun) => count == "1" ? $"1 {noun}" : $"{count} {noun}s";

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.0} KB",
        _ => $"{bytes} B",
    };

    private sealed record PipeResult(long ExitCode, long Bytes, bool SinkFailed, string? LastError);

    private sealed record ImportOutcome(string? Error, long Bytes, bool Cancelled);

    private sealed class LineBuffer
    {
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _text = new();

        public List<string> Add(byte[] buffer, int count)
        {
            var chars = new char[_decoder.GetCharCount(buffer, 0, count)];
            _decoder.GetChars(buffer, 0, count, chars, 0);
            _text.Append(chars);

            var content = _text.ToString();
            var end = content.LastIndexOf('\n');
            if (end < 0)
                return [];

            _text.Clear().Append(content[(end + 1)..]);
            return Lines(content[..end]);
        }

        public List<string> Flush()
        {
            var lines = Lines(_text.ToString());
            _text.Clear();
            return lines;
        }

        private static List<string> Lines(string text) =>
            [.. text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];
    }
}
