using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using Track.Configuration;
using Track.Services.Docker;
using Microsoft.Extensions.Options;

namespace Track.Website.Services;

/// <summary>
/// Moves a standalone agent to the central's version: starts a copy of its own container on the matching image
/// tag and removes itself once the copy has stayed up. Agents run by Swarm or Compose are left to them.
/// </summary>
public sealed partial class AgentSelfUpdater(
    ILogger<AgentSelfUpdater> logger,
    IOptions<TrackOptions> options,
    DockerClientFactory clientFactory)
{
    private const string PreviousSuffix = "-previous";
    private static readonly TimeSpan StableAfter = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(30);

    private readonly ILogger<AgentSelfUpdater> _logger = logger;
    private readonly IOptions<TrackOptions> _options = options;
    private readonly DockerClientFactory _clientFactory = clientFactory;

    private bool _cannotSelfUpdate;
    private string? _failedVersion;
    private DateTime _failedAt;

    public async Task UpdateIfBehindAsync(string? centralVersion, CancellationToken cancellationToken)
    {
        if (_cannotSelfUpdate || !_options.Value.AgentAutoUpdate || !ShouldUpdate(AppVersion.Build, centralVersion))
            return;
        if (centralVersion == _failedVersion && DateTime.UtcNow - _failedAt < RetryFailedAfter)
            return;

        var client = _clientFactory.GetClient(TrackOptions.LocalDockerSocket);
        var self = await FindSelfAsync(client, cancellationToken);
        if (self is null)
        {
            _cannotSelfUpdate = true;
            _logger.LogWarning("Can't find this agent's container, so it won't update itself to {Version}", centralVersion);
            return;
        }
        if (IsOrchestrated(self.Config.Labels))
        {
            _cannotSelfUpdate = true;
            _logger.LogInformation("Agent is run by Swarm or Compose; leaving its updates to them");
            return;
        }

        try
        {
            await ReplaceAsync(client, self, centralVersion!, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _failedVersion = centralVersion;
            _failedAt = DateTime.UtcNow;
            _logger.LogError(ex, "Agent update to {Version} failed; staying on {Current}", centralVersion, AppVersion.Build);
        }
    }

    private async Task ReplaceAsync(
        DockerClient client, ContainerInspectResponse self, string version, CancellationToken cancellationToken)
    {
        var repository = Repository(self.Config.Image);
        var image = $"{repository}:{version}";
        _logger.LogInformation("Updating agent from {Current} to {Image}", AppVersion.Build, image);

        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = repository, Tag = version }, null, new Progress<JSONMessage>(), cancellationToken);
        var currentImage = await client.Images.InspectImageAsync(self.Image, cancellationToken);

        var name = self.Name.TrimStart('/');
        await RemoveIfPresentAsync(client, name + PreviousSuffix, cancellationToken);
        await client.Containers.RenameContainerAsync(
            self.ID, new ContainerRenameParameters { NewName = name + PreviousSuffix }, cancellationToken);

        string? replacementId = null;
        try
        {
            var created = await client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Name = name,
                Image = image,
                User = self.Config.User,
                Env = OwnSettings(self.Config.Env, currentImage.Config?.Env),
                Labels = OwnLabels(self.Config.Labels, currentImage.Config?.Labels),
                Cmd = SameAs(self.Config.Cmd, currentImage.Config?.Cmd) ? null : self.Config.Cmd,
                Entrypoint = SameAs(self.Config.Entrypoint, currentImage.Config?.Entrypoint) ? null : self.Config.Entrypoint,
                HostConfig = self.HostConfig,
            }, cancellationToken);
            replacementId = created.ID;

            foreach (var (network, endpoint) in self.NetworkSettings?.Networks ?? new Dictionary<string, EndpointSettings>())
            {
                if (network == self.HostConfig?.NetworkMode)
                    continue;
                await client.Networks.ConnectNetworkAsync(network, new NetworkConnectParameters
                {
                    Container = replacementId,
                    EndpointConfig = new EndpointSettings { Aliases = endpoint.Aliases },
                }, cancellationToken);
            }

            await client.Containers.StartContainerAsync(replacementId, new ContainerStartParameters(), cancellationToken);
            await Task.Delay(StableAfter, cancellationToken);

            var replacement = await client.Containers.InspectContainerAsync(replacementId, cancellationToken);
            if (replacement.State is not { Running: true, Restarting: false })
                throw new InvalidOperationException(
                    $"The new agent container didn't stay up ({replacement.State?.Status}, exit {replacement.State?.ExitCode})");
        }
        catch
        {
            if (replacementId is not null)
                await client.Containers.RemoveContainerAsync(replacementId, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
            await client.Containers.RenameContainerAsync(self.ID, new ContainerRenameParameters { NewName = name }, CancellationToken.None);
            throw;
        }

        _logger.LogInformation("Agent {Image} is running; removing this container", image);
        // Removing our own container ends this process; the daemon finishes the removal.
        await client.Containers.RemoveContainerAsync(self.ID, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
    }

    private static async Task<ContainerInspectResponse?> FindSelfAsync(DockerClient client, CancellationToken cancellationToken)
    {
        var id = File.Exists("/proc/self/mountinfo")
            ? ParseContainerId(await File.ReadAllTextAsync("/proc/self/mountinfo", cancellationToken))
            : null;
        try
        {
            return await client.Containers.InspectContainerAsync(id ?? Environment.MachineName, cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            return null;
        }
    }

    private static async Task RemoveIfPresentAsync(DockerClient client, string name, CancellationToken cancellationToken)
    {
        try
        {
            await client.Containers.RemoveContainerAsync(name, new ContainerRemoveParameters { Force = true }, cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
        }
    }

    /// <summary>Only CI builds (four-part versions, which are also image tags) are followed.</summary>
    public static bool ShouldUpdate(string current, string? central) =>
        central is not null
        && !string.Equals(current, central, StringComparison.Ordinal)
        && System.Version.TryParse(central, out var target) && target.Revision >= 0;

    /// <summary>The image reference without its tag or digest; a registry port's colon is not a tag.</summary>
    public static string Repository(string image)
    {
        var withoutDigest = image.Split('@')[0];
        var colon = withoutDigest.LastIndexOf(':');
        return colon > withoutDigest.LastIndexOf('/') ? withoutDigest[..colon] : withoutDigest;
    }

    /// <summary>The container ID from the bind mounts Docker gives every container (hostname, resolv.conf).</summary>
    public static string? ParseContainerId(string mountInfo) =>
        ContainerIdInMounts().Match(mountInfo) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>The container's own env entries: what it was started with, minus what its image already sets.</summary>
    public static List<string> OwnSettings(IList<string>? container, IList<string>? image) =>
        [.. (container ?? []).Except(image ?? [])];

    private static Dictionary<string, string> OwnLabels(IDictionary<string, string>? container, IDictionary<string, string>? image) =>
        (container ?? new Dictionary<string, string>())
            .Where(l => image is null || !image.TryGetValue(l.Key, out var value) || value != l.Value)
            .ToDictionary(l => l.Key, l => l.Value);

    private static bool SameAs(IList<string>? container, IList<string>? image) =>
        (container ?? []).SequenceEqual(image ?? []);

    private static bool IsOrchestrated(IDictionary<string, string>? labels) =>
        labels is not null
        && (labels.ContainsKey(DockerLabels.StackNamespace)
            || labels.ContainsKey("com.docker.swarm.service.id")
            || labels.ContainsKey(DockerLabels.ComposeProject));

    [GeneratedRegex(@"/containers/([0-9a-f]{64})/")]
    private static partial Regex ContainerIdInMounts();
}
