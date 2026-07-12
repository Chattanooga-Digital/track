using Track.Enums;

namespace Track.Website.Api.v0.Models.S3;

/// <summary>Create/update payload for an S3 source.</summary>
public class S3SourceEditModel
{
    public required string Name { get; set; }
    public required string Endpoint { get; set; }
    public S3SourceKind Kind { get; set; }
    public required string AccessKey { get; set; }

    /// <summary>Plaintext secret. Required on create; on update, leave null/empty to keep the stored one.</summary>
    public string? SecretKey { get; set; }

    public string Region { get; set; } = "us-east-1";
    public bool Enabled { get; set; } = true;
}
