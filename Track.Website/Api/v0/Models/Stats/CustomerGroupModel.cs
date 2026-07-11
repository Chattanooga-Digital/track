namespace Track.Website.Api.v0.Models.Stats;

public class CustomerGroupModel
{
    /// <summary>Null for the group of stacks not assigned to any customer.</summary>
    public int? CustomerId { get; set; }

    public string? CustomerName { get; set; }
    public UsageTotalsModel Totals { get; set; } = new();
    public List<StackGroupModel> Stacks { get; set; } = [];
    public List<BucketUsageModel> Buckets { get; set; } = [];
}
