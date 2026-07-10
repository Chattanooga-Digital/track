using Track.Data.Models;
using Track.Enums;

namespace Track.Services;

public record NodeStatus(string Name, bool Online, string? Error, DateTime? LastSeen);

/// <summary>Total schedulable capacity of one Docker node.</summary>
public record NodeCapacity(string NodeName, long CpuCores, long MemoryBytes);

/// <summary>Result of one node's stats poll: its capacity plus a sample per running container.</summary>
public record NodeCollection(NodeCapacity Capacity, List<ContainerSample> Samples);

/// <summary>Summed capacity across all online nodes.</summary>
public record ClusterCapacity(int NodeCount, long CpuCores, long MemoryBytes);

public record CurrentState(
    List<ContainerSample> Samples,
    List<NodeStatus> Nodes,
    ClusterCapacity ClusterCapacity,
    Dictionary<string, long> ContainerLayerBytes,
    Dictionary<string, long> StackVolumeBytes);

/// <summary>Latest poll results per node, merged on read. Thread-safe.</summary>
public sealed class CurrentStateHolder
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<ContainerSample>> _samplesByNode = [];
    private readonly Dictionary<string, NodeStatus> _nodes = [];
    private readonly Dictionary<string, NodeCapacity> _capacityByNode = [];
    private readonly Dictionary<string, Dictionary<string, long>> _containerLayerBytesByNode = [];
    private readonly Dictionary<string, Dictionary<string, long>> _stackVolumeBytesByNode = [];

    public void SetNodeSamples(string nodeName, NodeCollection collection, DateTime timestamp)
    {
        lock (_lock)
        {
            _samplesByNode[nodeName] = collection.Samples;
            _capacityByNode[nodeName] = collection.Capacity;
            _nodes[nodeName] = new NodeStatus(nodeName, true, null, timestamp);
        }
    }

    public void SetNodeError(string nodeName, string error)
    {
        lock (_lock)
        {
            // Drop stale samples and capacity so an unreachable node's containers don't show as current.
            _samplesByNode.Remove(nodeName);
            _capacityByNode.Remove(nodeName);
            var lastSeen = _nodes.TryGetValue(nodeName, out var previous) ? previous.LastSeen : null;
            _nodes[nodeName] = new NodeStatus(nodeName, false, error, lastSeen);
        }
    }

    public void SetNodeDisk(string nodeName, List<DiskSample> samples)
    {
        lock (_lock)
        {
            _containerLayerBytesByNode[nodeName] = samples
                .Where(s => s.Kind == DiskUsageKind.ContainerLayer && s.ContainerId is not null)
                .ToDictionary(s => s.ContainerId!, s => s.SizeBytes);

            _stackVolumeBytesByNode[nodeName] = samples
                .Where(s => s.Kind == DiskUsageKind.Volume && s.StackName is not null)
                .GroupBy(s => s.StackName!)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.SizeBytes));
        }
    }

    public CurrentState GetCurrent()
    {
        lock (_lock)
        {
            var samples = _samplesByNode.Values.SelectMany(s => s).ToList();
            var nodes = _nodes.Values.OrderBy(n => n.Name).ToList();

            var clusterCapacity = new ClusterCapacity(
                _capacityByNode.Count,
                _capacityByNode.Values.Sum(c => c.CpuCores),
                _capacityByNode.Values.Sum(c => c.MemoryBytes));

            var containerLayerBytes = new Dictionary<string, long>();
            foreach (var (containerId, bytes) in _containerLayerBytesByNode.Values.SelectMany(d => d))
                containerLayerBytes[containerId] = bytes;

            var stackVolumeBytes = new Dictionary<string, long>();
            foreach (var (stackName, bytes) in _stackVolumeBytesByNode.Values.SelectMany(d => d))
                stackVolumeBytes[stackName] = stackVolumeBytes.GetValueOrDefault(stackName) + bytes;

            return new CurrentState(samples, nodes, clusterCapacity, containerLayerBytes, stackVolumeBytes);
        }
    }
}
