namespace Track.Enums;

public enum DiskUsageKind
{
    ContainerLayer = 0,
    Volume = 1,

    /// <summary>An S3 bucket's total object size, polled by the central from a configured S3 source.</summary>
    Bucket = 2,
}
