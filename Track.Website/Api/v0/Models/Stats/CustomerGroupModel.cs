namespace Track.Website.Api.v0.Models.Stats;

public class CustomerGroupModel
{
    /// <summary>Null for the two unbilled groups: shared infrastructure and stacks not assigned to anything.</summary>
    public int? CustomerId { get; set; }

    public bool IsShared { get; set; }

    public string? CustomerName { get; set; }
    public UsageTotalsModel Totals { get; set; } = new();
    public List<StackGroupModel> Stacks { get; set; } = [];
    public List<BucketUsageModel> Buckets { get; set; } = [];
}
