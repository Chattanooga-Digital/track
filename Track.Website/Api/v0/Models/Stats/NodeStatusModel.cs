namespace Track.Website.Api.v0.Models.Stats;

public class NodeStatusModel
{
    public required string Name { get; set; }
    public bool Online { get; set; }
    public string? Error { get; set; }
    public DateTime? LastSeen { get; set; }
}
