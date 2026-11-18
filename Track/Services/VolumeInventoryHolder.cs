using Track.Services.Ingest;

namespace Track.Services;

public sealed record NodeVolume(string VolumeName, string? StackName, long SizeBytes);

/// <summary>Per node: the volumes it holds (from disk reports) and which running containers mount them (from stats).</summary>
public sealed class VolumeInventoryHolder
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<NodeVolume>> _volumesByNode = [];
    private readonly Dictionary<string, List<VolumeMount>> _mountsByNode = [];

    public void SetVolumes(string nodeName, List<NodeVolume> volumes)
    {
        lock (_lock)
            _volumesByNode[nodeName] = volumes;
    }

    public void SetMounts(string nodeName, List<VolumeMount> mounts)
    {
        lock (_lock)
            _mountsByNode[nodeName] = mounts;
    }

    public void RemoveNode(string nodeName)
    {
        lock (_lock)
        {
            _volumesByNode.Remove(nodeName);
            _mountsByNode.Remove(nodeName);
        }
    }

    public (Dictionary<string, List<NodeVolume>> Volumes, Dictionary<string, List<VolumeMount>> Mounts) Get()
    {
        lock (_lock)
            return (new Dictionary<string, List<NodeVolume>>(_volumesByNode), new Dictionary<string, List<VolumeMount>>(_mountsByNode));
    }
}
