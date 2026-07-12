namespace Track.Enums;

/// <summary>How an S3 source is sized. Determines which API the collector uses per poll.</summary>
public enum S3SourceKind
{
    /// <summary>Generic S3: walk ListObjectsV2 and sum object sizes. Portable, but O(objects).</summary>
    GenericS3 = 0,

    /// <summary>MinIO/RustFS: one signed call to the admin data-usage API returns every bucket's size and count.</summary>
    Minio = 1,
}
