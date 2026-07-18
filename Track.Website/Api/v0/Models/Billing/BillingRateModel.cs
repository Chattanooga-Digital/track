namespace Track.Website.Api.v0.Models.Billing;

/// <summary>The pricing configuration, used both as the read shape and the edit payload.</summary>
public class BillingRateModel
{
    /// <summary>What the whole cluster costs per month; recovered in full from paying customers.</summary>
    public decimal ClusterComputeMonthlyCost { get; set; }

    /// <summary>Per-GB monthly rate for node-local disk. 0 treats local disk as bundled into the VM cost.</summary>
    public decimal DiskRatePerGbMonth { get; set; }

    /// <summary>Per-GB monthly rate for S3 object storage.</summary>
    public decimal ObjectStorageRatePerGbMonth { get; set; }

    public string Currency { get; set; } = "USD";
}
