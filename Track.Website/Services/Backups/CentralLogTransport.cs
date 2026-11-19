using RoushTech.Asio.Forwarding;
using Track.Services.Backups;

namespace Track.Website.Services.Backups;

/// <summary>Agent: sends job log lines to the central over the agent's own authenticated endpoint.</summary>
public sealed class CentralLogTransport(CentralClient centralClient, ILogger<CentralLogTransport> logger) : IAsioForwardingTransport
{
    private readonly CentralClient _centralClient = centralClient;
    private readonly ILogger<CentralLogTransport> _logger = logger;

    public string? Kind => "agent";

    public string? InstanceKey => null;

    public async Task SendAsync(AsioForwardFrame frame, CancellationToken cancellationToken)
    {
        if (frame is not AsioLogBatchPayload batch)
            return;

        try
        {
            await _centralClient.PostLogAsync(new AgentLogBatch(batch.SessionId, [.. batch.Lines.Select(l => new AgentLogLine(l.Message, l.Level))]), cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Live output is best-effort; the job's stored output still arrives with its result.
            _logger.LogDebug(ex, "Dropped {Count} job log lines", batch.Lines.Count);
        }
    }
}
