namespace Track.Services.Docker;

public static class DockerLabels
{
    public const string StackNamespace = "com.docker.stack.namespace";
    public const string ComposeProject = "com.docker.compose.project";
    public const string SwarmServiceName = "com.docker.swarm.service.name";
    public const string ComposeService = "com.docker.compose.service";

    public static string? GetStackName(IDictionary<string, string>? labels) =>
        GetLabel(labels, StackNamespace) ?? GetLabel(labels, ComposeProject);

    public static string? GetServiceName(IDictionary<string, string>? labels) =>
        GetLabel(labels, SwarmServiceName) ?? GetLabel(labels, ComposeService);

    private static string? GetLabel(IDictionary<string, string>? labels, string key) =>
        labels is not null && labels.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}
