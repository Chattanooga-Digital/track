namespace Track.Website.Api.v0.Models.Stats;

public class StackGroupModel
{
    /// <summary>Null for containers that are not part of any stack or compose project.</summary>
    public string? StackName { get; set; }

    public UsageTotalsModel Totals { get; set; } = new();
    public List<ContainerStatsModel> Containers { get; set; } = [];
}
