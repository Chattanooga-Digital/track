using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;

namespace Track.Services.Docker;

/// <summary>
/// Raw client for GET /system/df, which Docker.DotNet does not expose. Provides
/// per-container writable-layer sizes and per-volume sizes, over unix sockets or TCP.
/// </summary>
public sealed class DockerDfClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConcurrentDictionary<string, HttpClient> _clients = new();

    public async Task<DfDataUsage> GetDataUsageAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var client = _clients.GetOrAdd(endpoint, CreateClient);
        using var response = await client.GetAsync("/system/df", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<DfDataUsage>(stream, JsonOptions, cancellationToken)
            ?? new DfDataUsage();
    }

    private static HttpClient CreateClient(string endpoint)
    {
        var uri = new Uri(endpoint);

        if (uri.Scheme == "unix")
        {
            var socketPath = uri.LocalPath;
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                },
            };
            // Host is ignored over a unix socket but required for a valid request URI.
            return new HttpClient(handler) { BaseAddress = new Uri("http://localhost"), Timeout = TimeSpan.FromSeconds(120) };
        }

        var builder = new UriBuilder(uri) { Scheme = uri.Scheme == "tcp" ? "http" : uri.Scheme };
        return new HttpClient { BaseAddress = builder.Uri, Timeout = TimeSpan.FromSeconds(120) };
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        _clients.Clear();
    }
}

public class DfDataUsage
{
    public List<DfContainer>? Containers { get; set; }
    public List<DfVolume>? Volumes { get; set; }
}

public class DfContainer
{
    public string Id { get; set; } = string.Empty;
    public List<string>? Names { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public long SizeRw { get; set; }
    public long SizeRootFs { get; set; }
}

public class DfVolume
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string>? Labels { get; set; }
    public DfVolumeUsageData? UsageData { get; set; }
}

public class DfVolumeUsageData
{
    public long Size { get; set; }
    public long RefCount { get; set; }
}
