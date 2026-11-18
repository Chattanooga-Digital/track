namespace Track.Enums;

/// <summary>Whether an agent's token may receive backup jobs. Metrics are accepted in every state.</summary>
public enum AgentApproval
{
    Pending = 0,
    Approved = 1,
    Revoked = 2,
}
