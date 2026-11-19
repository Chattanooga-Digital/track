using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Track.Configuration;
using Track.Services.Backups;
using Track.Services.Ingest;
using Microsoft.Extensions.Options;

namespace Track.Website.Services;

/// <summary>Agent-side HTTP client that pushes local readings to the central instance's ingest endpoints.</summary>
public sealed class CentralClient(HttpClient httpClient, IOptions<TrackOptions> options)
{
    /// <summary>Header carrying the shared ingest secret. Matched against Track:Ingest:Key on the central side.</summary>
    public const string KeyHeader = "X-Track-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly TrackOptions _options = options.Value;

    /// <summary>Push stats and return the central's authoritative agent config (identity env vars).</summary>
    public async Task<IngestResponse?> PostStatsAsync(StatsReport report, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/v0/ingest/stats", report, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IngestResponse>(JsonOptions, cancellationToken);
    }

    public async Task PostDiskAsync(DiskReport report, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/v0/ingest/disk", report, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task PostSwarmAsync(SwarmReport report, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/v0/ingest/swarm", report, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Null when the job is no longer this node's to run.</summary>
    public async Task<AgentJob?> GetJobAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/api/v0/agent/jobs/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentJob>(JsonOptions, cancellationToken);
    }

    public async Task<AgentJobProgressResponse?> PostProgressAsync(long id, AgentJobProgress progress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"/api/v0/agent/jobs/{id}/progress", progress, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentJobProgressResponse>(JsonOptions, cancellationToken);
    }

    public async Task PostResultAsync(long id, AgentJobResult result, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"/api/v0/agent/jobs/{id}/result", result, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;
        response.EnsureSuccessStatusCode();
    }

    public async Task PostLogAsync(AgentLogBatch batch, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/v0/agent/jobs/log", batch, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Applies the base address and shared-secret header from configuration to the typed client.</summary>
    public static void Configure(IServiceProvider services, HttpClient client)
    {
        var options = services.GetRequiredService<IOptions<TrackOptions>>().Value;
        var url = options.Central.Url
            ?? throw new InvalidOperationException("Track:Central:Url must be set in Agent mode.");

        client.BaseAddress = new Uri(url);
        // Fail fast on a slow/hung central rather than stalling the whole poll tick on the 100s default.
        client.Timeout = TimeSpan.FromSeconds(30);
        if (!string.IsNullOrWhiteSpace(options.Central.Key))
            client.DefaultRequestHeaders.Add(KeyHeader, options.Central.Key);
        client.DefaultRequestHeaders.Add(AgentTokenStore.Header, services.GetRequiredService<AgentTokenStore>().Token);
    }
}
