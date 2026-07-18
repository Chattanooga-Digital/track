using Track.Data.Models;

namespace Track.Services.Billing;

/// <summary>One scope's dollar breakdown.</summary>
public readonly record struct BillingCosts(decimal ComputeCost, decimal DiskCost, decimal ObjectStorageCost)
{
    public decimal TotalCost => ComputeCost + DiskCost + ObjectStorageCost;
}

/// <summary>
/// Turns utilization into dollars. Compute is full cost recovery: a base rate spreads the fixed cluster
/// cost across paying customers by blended footprint, so their bills always sum to the cluster cost.
/// Storage is a flat per-GB markup. Cloud-billing convention: 1 GB = 1e9 bytes.
/// </summary>
public static class BillingCostCalculator
{
    public const decimal BytesPerGb = 1_000_000_000m;

    /// <summary>$ per blended point: the cluster cost divided by the total blended footprint of paying customers.</summary>
    public static decimal ComputeBaseRate(decimal clusterComputeMonthlyCost, double totalCustomerBlendedPercent) =>
        totalCustomerBlendedPercent > 0 ? clusterComputeMonthlyCost / (decimal)totalCustomerBlendedPercent : 0m;

    /// <summary>One scope's costs. Pass blended 0 for the unassigned group so it bills nothing.</summary>
    public static BillingCosts ForScope(
        double blendedPercent, decimal baseRate, double diskBytes, double objectStorageBytes, BillingRate rate)
    {
        var compute = baseRate * (decimal)blendedPercent;
        var disk = (decimal)diskBytes / BytesPerGb * rate.DiskRatePerGbMonth;
        var obj = (decimal)objectStorageBytes / BytesPerGb * rate.ObjectStorageRatePerGbMonth;
        return new BillingCosts(compute, disk, obj);
    }
}
