using Track.Configuration;
using Track.Services.Docker;
using Microsoft.Extensions.Options;

namespace Track.Website.Services;

/// <summary>
/// Resolves the stable identity an agent reports under. Priority: an explicit Track:NodeName, then the
/// Docker engine's own hostname (/info Name) which survives container restarts, and only as a last resort
/// the container hostname. Using the container hostname would mint a new node on every restart and leave
/// the previous one as a permanent phantom in the central UI.
/// </summary>
public sealed class AgentNodeNameResolver(DockerClientFactory clientFactory, IOptions<TrackOptions> options)
{
    private readonly DockerClientFactory _clientFactory = clientFactory;
    private readonly TrackOptions _options = options.Value;

    private string? _cached;

    public async Task<string> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.NodeName))
            return _options.NodeName;

        if (_cached is not null)
            return _cached;

        var client = _clientFactory.GetClient(TrackOptions.LocalDockerSocket);
        var info = await client.System.GetSystemInfoAsync(cancellationToken);
        return _cached = string.IsNullOrWhiteSpace(info.Name) ? Environment.MachineName : info.Name;
    }
}
