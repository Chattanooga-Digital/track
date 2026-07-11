namespace Track.Configuration;

public class DockerNodeOptions
{
    public required string Name { get; set; }

    /// <summary>Docker engine endpoint, e.g. unix:///var/run/docker.sock or tcp://node2:2375.</summary>
    public required string Endpoint { get; set; }
}

/// <summary>Which role this instance runs as. See <see cref="TrackMode"/>.</summary>
public enum TrackMode
{
    /// <summary>Serves the UI/API/SignalR and aggregates metrics pushed by agents. Does not poll Docker.</summary>
    Central,

    /// <summary>Polls the local Docker socket and pushes the readings to a central instance. No UI/DB.</summary>
    Agent,
}

/// <summary>Agent-side connection to the central instance it pushes readings to.</summary>
public class CentralConnectionOptions
{
    /// <summary>Base URL of the central instance, e.g. http://track:5020 on the swarm overlay network.</summary>
    public string? Url { get; set; }

    /// <summary>Shared secret sent on every push; must equal the central's <see cref="IngestOptions.Key"/>.</summary>
    public string? Key { get; set; }
}

/// <summary>Central-side ingest settings for accepting agent pushes.</summary>
public class IngestOptions
{
    /// <summary>Shared secret required on ingest requests. When unset, ingest is disabled (agents cannot push).</summary>
    public string? Key { get; set; }
}

/// <summary>
/// One S3-compatible endpoint (MinIO, AWS, ...) the central polls for per-bucket sizes. Each bucket
/// surfaces as a synthetic stack named "{Name}/{bucket}" that can be assigned to a customer like any
/// other stack.
/// </summary>
public class S3SourceOptions
{
    /// <summary>Short identifier for this source; prefixes the synthetic stack names, so keep it stable.</summary>
    public string? Name { get; set; }

    /// <summary>Service URL, e.g. http://minio:9000. Path-style addressing is always used (MinIO default).</summary>
    public string? Endpoint { get; set; }

    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    /// <summary>Signing region; MinIO accepts the default.</summary>
    public string Region { get; set; } = "us-east-1";
}

public class TrackOptions
{
    public const string Section = "Track";

    /// <summary>The local Docker engine socket, used by agents (and the historical single-node fallback).</summary>
    public const string LocalDockerSocket = "unix:///var/run/docker.sock";

    /// <summary>Central (default) aggregates pushed metrics; Agent polls the local socket and pushes them.</summary>
    public TrackMode Mode { get; set; } = TrackMode.Central;

    /// <summary>
    /// Name this node reports as. Agents default to the machine name; in Swarm set it to the node hostname
    /// with a service template: Track__NodeName={{.Node.Hostname}}.
    /// </summary>
    public string? NodeName { get; set; }

    /// <summary>Central marks a node offline if it has not pushed a reading within this window.</summary>
    public int NodeOfflineAfterSeconds { get; set; } = 45;

    /// <summary>Agent -> central connection (Agent mode only).</summary>
    public CentralConnectionOptions Central { get; set; } = new();

    /// <summary>Central-side ingest secret (Central mode only).</summary>
    public IngestOptions Ingest { get; set; } = new();

    /// <summary>
    /// Agent-side bootstrap for the ordered env-var identity list used to group label-less standalone
    /// containers (index 0 = highest priority). The value of the first matching env var becomes the
    /// container's stack name. The central overrides this at runtime via the push response; this seed
    /// only applies until the first response arrives. Only the resolved value is ever sent to the
    /// central - raw env (which holds secrets) never leaves the agent.
    /// </summary>
    public List<string> GroupingEnvVars { get; set; } = [];

    public int PollIntervalSeconds { get; set; } = 15;
    public int DiskPollIntervalSeconds { get; set; } = 300;
    public int RetentionDays { get; set; } = 14;

    /// <summary>S3-compatible endpoints to poll for bucket sizes (Central mode only).</summary>
    public List<S3SourceOptions> S3Sources { get; set; } = [];

    /// <summary>
    /// How often bucket sizes are polled. Sizing walks every object in every bucket
    /// (one request per 1000 objects), so this stays on a slow cadence.
    /// </summary>
    public int S3PollIntervalSeconds { get; set; } = 900;

    /// <summary>Seed admin username. The account is created on first startup if no users exist.</summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>Seed admin email (Identity requires one).</summary>
    public string? AdminEmail { get; set; } = "admin@chattanooga.digital";

    /// <summary>Seed admin password. Change this in production via config or the Track__AdminPassword env var.</summary>
    public string AdminPassword { get; set; } = "admin";

    /// <summary>
    /// Directory to persist Data Protection keys (which sign the auth cookie). Set this to a
    /// path on a mounted volume so sessions survive restarts; null uses the framework default.
    /// </summary>
    public string? DataProtectionKeysPath { get; set; }
}
