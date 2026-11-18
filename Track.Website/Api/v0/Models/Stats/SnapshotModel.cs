namespace Track.Website.Api.v0.Models.Stats;

public class SnapshotModel
{
    public DateTime Timestamp { get; set; }

    /// <summary>The central's build version, which agents are expected to match.</summary>
    public string CentralVersion { get; set; } = AppVersion.Build;

    public ClusterCapacityModel Cluster { get; set; } = new();
    public UsageTotalsModel Totals { get; set; } = new();
    public List<NodeStatusModel> Nodes { get; set; } = [];
    public List<CustomerGroupModel> Customers { get; set; } = [];

    /// <summary>Backup volumes that are failing, stale, or otherwise need a look.</summary>
    public int BackupsNeedingAttention { get; set; }

    /// <summary>Billing currency (ISO code) for the cost figures on the usage totals.</summary>
    public string Currency { get; set; } = "USD";
}
