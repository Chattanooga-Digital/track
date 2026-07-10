using System.Collections.Concurrent;
using global::Docker.DotNet;

namespace Track.Services.Docker;

public sealed class DockerClientFactory : IDisposable
{
    private readonly ConcurrentDictionary<string, DockerClient> _clients = new();

    public DockerClient GetClient(string endpoint) =>
        _clients.GetOrAdd(endpoint, e => new DockerClientConfiguration(new Uri(e)).CreateClient());

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        _clients.Clear();
    }
}
