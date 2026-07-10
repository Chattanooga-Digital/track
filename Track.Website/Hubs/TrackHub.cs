using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Track.Website.Hubs;

[Authorize]
public class TrackHub : Hub
{
    public const string HubPath = "/hubs/metrics";

    /// <summary>Event name for the grouped snapshot broadcast after every stats poll.</summary>
    public const string SnapshotEvent = "snapshot";
}
