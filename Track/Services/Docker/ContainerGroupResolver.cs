using System.Collections.Concurrent;
using global::Docker.DotNet;
using Microsoft.Extensions.Logging;

namespace Track.Services.Docker;

/// <summary>
/// Result of resolving one container's group. <see cref="LabelLess"/> distinguishes standalone
/// <c>docker run</c> containers (no stack/compose label) - only those are surfaced in the inventory.
/// </summary>
public readonly record struct GroupResolution(string? StackName, bool LabelLess, IReadOnlyList<string> EnvKeys);

/// <summary>
/// Derives a container's stack name. Swarm/Compose containers use their label (no inspect). Label-less
/// standalone containers are inspected once (env is immutable for a container's lifetime, so cached by ID)
/// and grouped by the value of the first configured identity env var. Env <b>key names</b> are exposed for
/// the inventory; env <b>values</b> stay private except the single resolved identity value - the container's
/// secrets never leave this class.
/// </summary>
public sealed class ContainerGroupResolver(
    DockerClientFactory clientFactory,
    IdentityEnvVarProvider identityEnvVars,
    ILogger<ContainerGroupResolver> logger)
{
    // StackName column length; a hostname is always shorter, but guard defensively.
    private const int MaxStackNameLength = 200;

    // Parsed env per container ID. Immutable for the container's lifetime, so inspected at most once.
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _envByContainer = new();

    private readonly DockerClientFactory _clientFactory = clientFactory;
    private readonly IdentityEnvVarProvider _identityEnvVars = identityEnvVars;
    private readonly ILogger<ContainerGroupResolver> _logger = logger;

    public async Task<GroupResolution> ResolveAsync(
        string endpoint, string containerId, IDictionary<string, string>? labels, CancellationToken cancellationToken)
    {
        var label = DockerLabels.GetStackName(labels);
        if (label is not null)
            return new GroupResolution(label, LabelLess: false, EnvKeys: []);

        var env = await GetEnvAsync(endpoint, containerId, cancellationToken);
        if (env is null)
            return new GroupResolution(null, LabelLess: true, EnvKeys: []);

        // Never surface secret-looking keys as grouping candidates.
        var safeKeys = env.Keys.Where(key => !SensitiveEnvKeys.IsSensitive(key)).ToList();
        return new GroupResolution(ResolveFromEnv(env), LabelLess: true, EnvKeys: safeKeys);
    }

    /// <summary>Drop cached env for containers no longer present, keeping the cache bounded to live containers.</summary>
    public void RetainOnly(IReadOnlySet<string> liveContainerIds)
    {
        foreach (var id in _envByContainer.Keys)
        {
            if (!liveContainerIds.Contains(id))
                _envByContainer.TryRemove(id, out _);
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> GetEnvAsync(
        string endpoint, string containerId, CancellationToken cancellationToken)
    {
        if (_envByContainer.TryGetValue(containerId, out var cached))
            return cached;

        try
        {
            var client = _clientFactory.GetClient(endpoint);
            var inspect = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
            var env = ParseEnv(inspect.Config?.Env);
            _envByContainer[containerId] = env;
            return env;
        }
        catch (DockerApiException ex)
        {
            // The container stopped between listing and the inspect; skip it this tick.
            _logger.LogDebug(ex, "Inspect unavailable for container {ContainerId}", containerId);
            return null;
        }
    }

    private string? ResolveFromEnv(IReadOnlyDictionary<string, string> env) =>
        ResolveIdentity(_identityEnvVars.Current, env);

    /// <summary>Value of the first identity env var present and non-empty, truncated to the stack-name length.</summary>
    public static string? ResolveIdentity(
        IReadOnlyList<string> identityEnvVars, IReadOnlyDictionary<string, string> env)
    {
        foreach (var name in identityEnvVars)
        {
            // Refuse to key on a secret even if it was somehow configured - its value must never leak.
            if (SensitiveEnvKeys.IsSensitive(name))
                continue;

            if (env.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                return value.Length > MaxStackNameLength ? value[..MaxStackNameLength] : value;
        }

        return null;
    }

    /// <summary>Parse a container's <c>KEY=value</c> env lines into a case-sensitive dictionary.</summary>
    public static IReadOnlyDictionary<string, string> ParseEnv(IList<string>? envLines)
    {
        // Docker env keys are case-sensitive; match exactly.
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in envLines ?? [])
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
                env[line[..separator]] = line[(separator + 1)..];
        }

        return env;
    }
}
