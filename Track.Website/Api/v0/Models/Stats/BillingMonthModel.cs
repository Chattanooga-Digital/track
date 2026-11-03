namespace Track.Website.Api.v0.Models.Stats;

/// <summary>One calendar month's billing: per-customer averages over the month's billing ticks.</summary>
public class BillingMonthModel
{
    /// <summary>The month in yyyy-MM form.</summary>
    public required string Month { get; set; }

    public DateTime From { get; set; }
    public DateTime To { get; set; }

    /// <summary>Billing ticks recorded in the month (the averaging denominator); 0 means no data.</summary>
    public int SampleCount { get; set; }

    public BillingCapacityModel Capacity { get; set; } = new();
    public List<BillingCustomerModel> Customers { get; set; } = [];

    /// <summary>Billing currency (ISO code) in effect for the figures below.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Sum of every customer's compute charge; equals the recovered cluster cost when usage was nonzero.</summary>
    public decimal ComputeCost { get; set; }
    public decimal DiskCost { get; set; }
    public decimal ObjectStorageCost { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>Average cluster capacity over the month's ticks (fractional when nodes came and went).</summary>
public class BillingCapacityModel
{
    public double NodeCount { get; set; }
    public double CpuCores { get; set; }
    public double MemoryBytes { get; set; }
}

/// <summary>
/// One customer's month-average usage. Averages divide by the month's total tick count, so a
/// customer present for half the month bills half of its while-present average. A null
/// <see cref="CustomerId"/> is an unbilled group: shared infrastructure when <see cref="IsShared"/>,
/// otherwise the unassigned remainder.
/// </summary>
public class BillingCustomerModel
{
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public bool IsShared { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryUsedBytes { get; set; }
    public double DiskBytes { get; set; }
    public double ObjectStorageBytes { get; set; }
    public double CpuPercentOfCluster { get; set; }
    public double MemoryPercentOfCluster { get; set; }
    public double BlendedPercentOfCluster { get; set; }

    /// <summary>Compute charge: share of the recovered cluster cost. $0 for the unbilled groups.</summary>
    public decimal ComputeCost { get; set; }
    public decimal DiskCost { get; set; }
    public decimal ObjectStorageCost { get; set; }
    public decimal TotalCost { get; set; }
}
