using Track.Website.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Track.Website.Services.Backups;

/// <summary>Tells open Backups pages to reload.</summary>
public sealed class BackupNotifier(IHubContext<TrackHub> hubContext)
{
    private readonly IHubContext<TrackHub> _hubContext = hubContext;

    public Task ChangedAsync(CancellationToken cancellationToken) =>
        _hubContext.Clients.All.SendAsync(TrackHub.BackupsEvent, cancellationToken);
}
