using System.Diagnostics;
using System.Text;
using Track.Configuration;
using Track.Services.Backups;
using Microsoft.Extensions.Options;

namespace Track.Website.Services.Backups;

public sealed record ResticResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    public string Message =>
        StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
        ?? $"restic exited with code {ExitCode}";
}

/// <summary>Central: runs the restic binary bundled in the image, for repository setup, maintenance and downloads.</summary>
public sealed class ResticRunner(IOptions<TrackOptions> options, ILogger<ResticRunner> logger)
{
    public const int RepositoryMissingExitCode = 10;

    private readonly string _resticPath = options.Value.Backups.ResticPath;
    private readonly ILogger<ResticRunner> _logger = logger;

    public async Task<string> VersionAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_resticPath, "version") { RedirectStandardOutput = true, UseShellExecute = false };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Couldn't start {_resticPath}");
        var version = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return version.Trim();
    }

    public async Task<ResticResult> RunAsync(
        ResticTarget target, IEnumerable<string> arguments, CancellationToken cancellationToken, Stream? output = null)
    {
        var startInfo = new ProcessStartInfo(_resticPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments.Concat(target.Options))
            startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in target.Environment)
            startInfo.Environment[key] = value;
        // Outside the data volume, so restic's cache never ends up in Track's own backups.
        startInfo.Environment["RESTIC_CACHE_DIR"] = Path.Combine(Path.GetTempPath(), "restic-cache");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Couldn't start {_resticPath}");
        try
        {
            var error = ReadErrorsAsync(process.StandardError, cancellationToken);
            string standardOutput;
            if (output is null)
            {
                standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            }
            else
            {
                await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
                standardOutput = "";
            }

            await process.WaitForExitAsync(cancellationToken);
            return new ResticResult(process.ExitCode, standardOutput, await error);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    // Logged line by line so a job's live log shows restic retrying, rather than nothing until it gives up.
    private async Task<string> ReadErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            text.AppendLine(line);
            if (line.Length > 0)
                _logger.LogInformation("{Line}", line);
        }

        return text.ToString();
    }
}
