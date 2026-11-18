namespace Track.Enums;

/// <summary>How bucket names are put into S3 requests. Auto uses virtual-host style for AWS and path style elsewhere.</summary>
public enum S3Addressing
{
    Auto = 0,
    VirtualHost = 1,
    Path = 2,
}
