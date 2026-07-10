namespace Track.Website.Api.v0.Models.Stats;

public class SnapshotModel
{
    public DateTime Timestamp { get; set; }
    public ClusterCapacityModel Cluster { get; set; } = new();
    public UsageTotalsModel Totals { get; set; } = new();
    public List<NodeStatusModel> Nodes { get; set; } = [];
    public List<CustomerGroupModel> Customers { get; set; } = [];
}
