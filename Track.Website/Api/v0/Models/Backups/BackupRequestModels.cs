using Track.Enums;

namespace Track.Website.Api.v0.Models.Backups;

public class VolumeKeyModel
{
    public required string Key { get; set; }
}

public class VolumeOverrideModel
{
    public required string VolumeName { get; set; }

    /// <summary>True excludes, false includes despite the labels, null hands the choice back to the labels.</summary>
    public bool? Excluded { get; set; }
}

public class RestoreRequestModel
{
    public long SnapshotId { get; set; }
    public RestoreMode Mode { get; set; }

    /// <summary>For a new volume: the node to create it on. Defaults to the volume's own node.</summary>
    public string? NodeName { get; set; }

    /// <summary>Replace only: must equal the volume name.</summary>
    public string? ConfirmVolumeName { get; set; }
}
