using Track.Services.Ingest;

namespace Track.Services;

/// <summary>
/// Central live inventory of label-less standalone containers pushed by agents, keyed by node. Not persisted
/// - it mirrors current state so the UI can offer each container's env var keys as a grouping identity.
/// Replaced per push; a node's entry is dropped when the reaper marks it offline.
/// </summary>
public sealed class ContainerInventoryHolder
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<LabelLessContainer>> _byNode = [];

    public void SetNodeInventory(string nodeName, List<LabelLessContainer> inventory)
    {
        lock (_lock)
        {
            _byNode[nodeName] = inventory;
        }
    }

    public void RemoveNodes(IEnumerable<string> nodeNames)
    {
        lock (_lock)
        {
            foreach (var name in nodeNames)
                _byNode.Remove(name);
        }
    }

    /// <summary>Every label-less container across nodes, paired with the node it runs on.</summary>
    public List<(string NodeName, LabelLessContainer Container)> GetAll()
    {
        lock (_lock)
        {
            return _byNode
                .SelectMany(entry => entry.Value.Select(container => (entry.Key, container)))
                .ToList();
        }
    }
}
