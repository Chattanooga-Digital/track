using Track.Enums;

namespace Track.Website.Api.v0.Models.Backups;

public class BackupTargetModel
{
    public bool Configured { get; set; }
    public string Endpoint { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "";
    public string Prefix { get; set; } = "track/";
    public S3Addressing Addressing { get; set; }
    public string AgentAccessKey { get; set; } = "";
    public bool HasAgentSecret { get; set; }
    public string? MaintenanceAccessKey { get; set; }
    public bool HasMaintenanceSecret { get; set; }
    public bool MasterKeySet { get; set; }
    public bool MasterKeyFromEnvironment { get; set; }
    public required string MasterKeySecretName { get; set; }
    public List<BackupRepositoryModel> Repositories { get; set; } = [];
}

public class BackupTargetEditModel
{
    public string Endpoint { get; set; } = "";
    public string Region { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string Prefix { get; set; } = "";
    public S3Addressing Addressing { get; set; }
    public string AgentAccessKey { get; set; } = "";

    /// <summary>Blank keeps the stored secret.</summary>
    public string? AgentSecretKey { get; set; }

    public string? MaintenanceAccessKey { get; set; }
    public string? MaintenanceSecretKey { get; set; }
}

public class BackupTargetCheckModel
{
    public required string Label { get; set; }
    public bool Ok { get; set; }
    public string? Detail { get; set; }
}

public class BackupRepositoryModel
{
    public required string Key { get; set; }
    public string? CustomerName { get; set; }
    public bool IsShared { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? LastPrunedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public string? LastMaintenanceError { get; set; }
}
