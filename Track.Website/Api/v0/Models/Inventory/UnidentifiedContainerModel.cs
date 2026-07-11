namespace Track.Website.Api.v0.Models.Inventory;

/// <summary>
/// A label-less standalone container that has not yet resolved to a group, offered to the UI so the operator
/// can pick one of its env var keys as a grouping identity. Carries key names only - never env values.
/// </summary>
public class UnidentifiedContainerModel
{
    public required string NodeName { get; set; }
    public required string ContainerId { get; set; }
    public required string ContainerName { get; set; }
    public string? Image { get; set; }
    public required List<string> EnvKeys { get; set; }
}
