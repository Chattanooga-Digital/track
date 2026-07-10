namespace Track.Configuration;

public class DockerNodeOptions
{
    public required string Name { get; set; }

    /// <summary>Docker engine endpoint, e.g. unix:///var/run/docker.sock or tcp://node2:2375.</summary>
    public required string Endpoint { get; set; }
}

public class TrackOptions
{
    public const string Section = "Track";

    public int PollIntervalSeconds { get; set; } = 15;
    public int DiskPollIntervalSeconds { get; set; } = 300;
    public int RetentionDays { get; set; } = 14;
    public string[] CorsOrigins { get; set; } = ["http://localhost:5173", "https://localhost:5173"];
    public List<DockerNodeOptions> Nodes { get; set; } = [];

    /// <summary>Seed admin username. The account is created on first startup if no users exist.</summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>Seed admin email (Identity requires one). Defaults from the username.</summary>
    public string? AdminEmail { get; set; }

    /// <summary>Seed admin password. Change this in production via config or the Track__AdminPassword env var.</summary>
    public string AdminPassword { get; set; } = "admin";

    /// <summary>
    /// Directory to persist Data Protection keys (which sign the auth cookie). Set this to a
    /// path on a mounted volume so sessions survive restarts; null uses the framework default.
    /// </summary>
    public string? DataProtectionKeysPath { get; set; }

    /// <summary>Configured nodes, or the local Docker socket when none are configured.</summary>
    public List<DockerNodeOptions> ResolveNodes() => Nodes.Count > 0
        ? Nodes
        : [new DockerNodeOptions { Name = Environment.MachineName, Endpoint = "unix:///var/run/docker.sock" }];
}
