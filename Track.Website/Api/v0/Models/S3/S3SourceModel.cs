using Track.Enums;

namespace Track.Website.Api.v0.Models.S3;

/// <summary>An S3 source as returned to clients. The secret key is never included.</summary>
public class S3SourceModel
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Endpoint { get; set; }
    public S3SourceKind Kind { get; set; }
    public required string AccessKey { get; set; }
    public string Region { get; set; } = "us-east-1";
    public bool Enabled { get; set; }

    /// <summary>Whether a secret key is stored (so the UI can show "set" without revealing it).</summary>
    public bool HasSecret { get; set; }

    public DateTime CreatedAt { get; set; }
}
