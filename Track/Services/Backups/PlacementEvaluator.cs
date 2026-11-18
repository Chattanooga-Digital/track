using Track.Services.Ingest;

namespace Track.Services.Backups;

/// <summary>Works out which nodes a swarm service may run on from its placement constraints.</summary>
public static class PlacementEvaluator
{
    /// <summary>The eligible nodes' hostnames, or null when a constraint uses an attribute Track doesn't know.</summary>
    public static List<string>? EligibleNodes(IEnumerable<string> constraints, IEnumerable<SwarmNodeInfo> nodes)
    {
        var parsed = new List<(string Key, bool Equal, string Value)>();
        foreach (var constraint in constraints)
        {
            var op = constraint.Contains("==", StringComparison.Ordinal) ? "=="
                : constraint.Contains("!=", StringComparison.Ordinal) ? "!="
                : null;
            if (op is null)
                return null;
            var parts = constraint.Split(op, 2, StringSplitOptions.TrimEntries);
            parsed.Add((parts[0], op == "==", parts[1]));
        }

        var eligible = new List<string>();
        foreach (var node in nodes.Where(n => string.Equals(n.Availability, "active", StringComparison.OrdinalIgnoreCase)))
        {
            var matches = true;
            foreach (var (key, equal, value) in parsed)
            {
                var actual = Attribute(node, key);
                if (actual is null && !key.StartsWith("node.labels.", StringComparison.Ordinal))
                    return null;
                if (string.Equals(actual, value, StringComparison.OrdinalIgnoreCase) != equal)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                eligible.Add(node.Hostname);
        }

        return eligible;
    }

    private static string? Attribute(SwarmNodeInfo node, string key) => key switch
    {
        "node.hostname" => node.Hostname,
        "node.role" => node.Role,
        _ when key.StartsWith("node.labels.", StringComparison.Ordinal) =>
            node.Labels.GetValueOrDefault(key["node.labels.".Length..]),
        _ => null,
    };
}
