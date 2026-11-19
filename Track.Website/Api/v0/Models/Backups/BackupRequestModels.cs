using Track.Enums;

namespace Track.Website.Api.v0.Models.Backups;

public class VolumeKeyModel
{
    public required string Key { get; set; }
}

public class VolumeOverrideModel
{
    public List<string> VolumeNames { get; set; } = [];

    /// <summary>True ignores, false backs up despite the labels or being empty, null hands the choice back.</summary>
    public bool? Excluded { get; set; }
}

public class VolumeMethodModel
{
    public required string VolumeName { get; set; }

    /// <summary>Null hands the choice back to the labels and the detected database.</summary>
    public BackupMethod? Method { get; set; }
}

public class VolumeTestModel
{
    public required string Key { get; set; }

    /// <summary>The snapshot to test; the latest dump when left out.</summary>
    public long? SnapshotId { get; set; }
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
