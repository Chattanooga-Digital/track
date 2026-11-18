using System.Text.RegularExpressions;
using Track.Services.Ingest;

namespace Track.Services.Backups;

public enum CatalogVolumeKind
{
    /// <summary>One live copy, backed up from the node that uses it.</summary>
    Single,

    /// <summary>A separate volume of the same name on each node, such as one used by a global service.</summary>
    PerNode,

    /// <summary>A leftover copy on a node that doesn't use it. Not backed up.</summary>
    StaleCopy,

    /// <summary>Copies on several nodes and none in use, so there is no telling which is current.</summary>
    Ambiguous,
}

public enum ExclusionSource
{
    Label,
    Track,
}

public sealed record CatalogVolume(
    string Key,
    string VolumeName,
    string? NodeName,
    CatalogVolumeKind Kind,
    string? StackName,
    List<string> Services,
    List<string> Images,
    long? SizeBytes,
    ExclusionSource? ExcludedBy,
    bool? Override,
    List<string> Paths,
    List<AgentHook> PreHooks,
    List<AgentHook> PostHooks,
    List<AgentHook> RestorePreHooks,
    List<AgentHook> RestorePostHooks,
    bool Running,
    bool Global,
    List<string> Copies)
{
    public bool Excluded => ExcludedBy is not null;

    public bool Protected => !Excluded && Kind is CatalogVolumeKind.Single or CatalogVolumeKind.PerNode;

    public bool DatabaseWithoutHook => PreHooks.Count == 0 && Images.Any(DatabaseImages.IsDatabase);
}

public sealed record CatalogInputs(
    IReadOnlyDictionary<string, List<NodeVolume>> VolumesByNode,
    IReadOnlyDictionary<string, List<VolumeMount>> MountsByNode,
    SwarmReport? Swarm,
    IReadOnlyDictionary<string, bool> Overrides,
    IReadOnlySet<string> IgnoredVolumes);

/// <summary>Turns what agents report into the list of volumes to back up, one entry per copy that matters.</summary>
public static partial class BackupCatalog
{
    public static string RestoreVolumeName(string volumeName, DateTime nowUtc) => $"{volumeName}-restore-{nowUtc:yyyyMMdd-HHmmss}";

    public static List<CatalogVolume> Build(CatalogInputs inputs)
    {
        var names = inputs.VolumesByNode.Values.SelectMany(v => v.Select(x => x.VolumeName))
            .Concat(inputs.MountsByNode.Values.SelectMany(m => m.Select(x => x.VolumeName)))
            .Where(name => !inputs.IgnoredVolumes.Contains(name) && !RestoredVolume().IsMatch(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        var catalog = new List<CatalogVolume>();
        foreach (var name in names)
        {
            var mounts = inputs.MountsByNode
                .SelectMany(entry => entry.Value.Where(m => m.VolumeName == name).Select(m => (Node: entry.Key, Mount: m)))
                .ToList();
            var mountNodes = mounts.Select(m => m.Node).Distinct().Order(StringComparer.Ordinal).ToList();

            // Anonymous volumes nothing uses are leftovers from removed containers.
            if (mountNodes.Count == 0 && AnonymousVolume().IsMatch(name))
                continue;

            var copies = inputs.VolumesByNode
                .Where(entry => entry.Value.Any(v => v.VolumeName == name))
                .Select(entry => entry.Key)
                .Concat(mountNodes)
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();
            var services = inputs.Swarm?.Services.Where(s => s.Volumes.Contains(name)).ToList() ?? [];
            var global = services.Any(s => s.Global);

            CatalogVolume Entry(string key, string? node, CatalogVolumeKind kind) =>
                BuildEntry(inputs, key, name, node, kind, mounts, services, global, copies);

            if (global || mountNodes.Count > 1)
            {
                catalog.AddRange(copies.Select(node => Entry($"{name}@{node}", node, CatalogVolumeKind.PerNode)));
            }
            else if (mountNodes.Count == 1)
            {
                catalog.Add(Entry(name, mountNodes[0], CatalogVolumeKind.Single));
                catalog.AddRange(copies.Where(node => node != mountNodes[0])
                    .Select(node => Entry($"{name}@{node}", node, CatalogVolumeKind.StaleCopy)));
            }
            else if (copies.Count == 1)
            {
                catalog.Add(Entry(name, copies[0], CatalogVolumeKind.Single));
            }
            else
            {
                catalog.Add(Entry(name, null, CatalogVolumeKind.Ambiguous));
            }
        }

        return catalog;
    }

    private static CatalogVolume BuildEntry(
        CatalogInputs inputs,
        string key,
        string name,
        string? node,
        CatalogVolumeKind kind,
        List<(string Node, VolumeMount Mount)> mounts,
        List<SwarmServiceInfo> services,
        bool global,
        List<string> copies)
    {
        var nodeMounts = mounts.Where(m => node is null || m.Node == node).Select(m => m.Mount).ToList();
        var volume = node is not null ? inputs.VolumesByNode.GetValueOrDefault(node)?.FirstOrDefault(v => v.VolumeName == name) : null;
        var stack = volume?.StackName
            ?? inputs.VolumesByNode.Values.SelectMany(v => v).FirstOrDefault(v => v.VolumeName == name && v.StackName is not null)?.StackName
            ?? nodeMounts.Select(m => m.StackName).FirstOrDefault(s => s is not null)
            ?? services.Select(s => s.StackName).FirstOrDefault(s => s is not null);

        var labelsByService = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var images = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var mount in nodeMounts)
        {
            Merge(labelsByService, mount.ServiceName ?? mount.ContainerName, mount.BackupLabels);
            if (mount.Image is not null)
                images.Add(mount.Image);
        }
        foreach (var service in services)
        {
            Merge(labelsByService, service.Name, service.BackupLabels);
            if (service.Image is not null)
                images.Add(service.Image);
        }

        if (kind == CatalogVolumeKind.StaleCopy)
        {
            return new CatalogVolume(key, name, node, kind, stack, [], [], volume?.SizeBytes, null, null,
                [], [], [], [], [], false, global, copies);
        }

        ExclusionSource? excludedBy = null;
        bool? overridden = inputs.Overrides.TryGetValue(name, out var excluded) ? excluded : null;
        if (overridden == true)
            excludedBy = ExclusionSource.Track;
        else if (overridden is null && labelsByService.Values.Any(labels =>
                     BackupLabels.ExcludesService(labels) || BackupLabels.ExcludesVolume(labels, name, stack)))
            excludedBy = ExclusionSource.Label;

        List<AgentHook> Hooks(string label) => labelsByService
            .Select(entry => BackupLabels.Hook(entry.Value, label) is { } command ? new AgentHook(entry.Key, command) : null)
            .OfType<AgentHook>()
            .ToList();

        return new CatalogVolume(
            key,
            name,
            node,
            kind,
            stack,
            [.. labelsByService.Keys],
            [.. images],
            volume?.SizeBytes,
            excludedBy,
            overridden,
            labelsByService.Values.SelectMany(labels => BackupLabels.Paths(labels, name, stack)).Distinct().ToList(),
            Hooks(BackupLabels.PreHook),
            Hooks(BackupLabels.PostHook),
            Hooks(BackupLabels.RestorePreHook),
            Hooks(BackupLabels.RestorePostHook),
            nodeMounts.Count > 0,
            global,
            copies);
    }

    private static void Merge(Dictionary<string, Dictionary<string, string>> labelsByService, string service, Dictionary<string, string> labels)
    {
        if (!labelsByService.TryGetValue(service, out var merged))
            labelsByService[service] = merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in labels)
            merged[key] = value;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex AnonymousVolume();

    [GeneratedRegex(@"-restore-\d{8}-\d{6}$")]
    private static partial Regex RestoredVolume();
}
