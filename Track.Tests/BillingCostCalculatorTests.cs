using Track.Data.Models;
using Track.Services.Billing;

namespace Track.Tests;

public class BillingCostCalculatorTests
{
    [Test]
    public void ComputeBaseRate_ZeroFootprint_IsZero()
    {
        Assert.That(BillingCostCalculator.ComputeBaseRate(1000m, 0), Is.EqualTo(0m));
    }

    [Test]
    public void ComputeBaseRate_DividesCostByTotalBlended()
    {
        // $1000 spread over 25 blended points = $40 per point.
        Assert.That(BillingCostCalculator.ComputeBaseRate(1000m, 25), Is.EqualTo(40m));
    }

    [Test]
    public void ForScope_ZeroRates_ProduceZeroCost()
    {
        var costs = BillingCostCalculator.ForScope(50, baseRate: 0m, diskBytes: 5_000_000_000, objectStorageBytes: 9_000_000_000, new BillingRate());
        Assert.That(costs.TotalCost, Is.EqualTo(0m));
    }

    [Test]
    public void ForScope_ComputesEachLineItem()
    {
        var rate = new BillingRate { DiskRatePerGbMonth = 0.05m, ObjectStorageRatePerGbMonth = 0.02m };
        var costs = BillingCostCalculator.ForScope(
            blendedPercent: 10, baseRate: 40m, diskBytes: 2_000_000_000, objectStorageBytes: 1_000_000_000, rate);

        Assert.That(costs.ComputeCost, Is.EqualTo(400m));       // 40 x 10
        Assert.That(costs.DiskCost, Is.EqualTo(0.10m));          // 2 GB x 0.05
        Assert.That(costs.ObjectStorageCost, Is.EqualTo(0.02m)); // 1 GB x 0.02
        Assert.That(costs.TotalCost, Is.EqualTo(400.12m));
    }

    [Test]
    public void ForScope_GbBoundary_IsOneBillionBytes()
    {
        var rate = new BillingRate { DiskRatePerGbMonth = 1m };
        var costs = BillingCostCalculator.ForScope(0, 0m, diskBytes: 1_000_000_000, objectStorageBytes: 0, rate);
        Assert.That(costs.DiskCost, Is.EqualTo(1m));
    }

    [Test]
    public void CostRecovery_CustomerComputeChargesSumToClusterCost()
    {
        // Two customers at 10 and 30 blended: base rate = 1000 / 40 = 25/point.
        var total = 10 + 30;
        var baseRate = BillingCostCalculator.ComputeBaseRate(1000m, total);
        var a = BillingCostCalculator.ForScope(10, baseRate, 0, 0, new BillingRate()).ComputeCost;
        var b = BillingCostCalculator.ForScope(30, baseRate, 0, 0, new BillingRate()).ComputeCost;

        Assert.That(a, Is.EqualTo(250m));
        Assert.That(b, Is.EqualTo(750m));
        Assert.That(a + b, Is.EqualTo(1000m)); // full recovery regardless of how idle the cluster is
    }
}
