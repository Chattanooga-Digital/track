using Track.Data;
using Track.Data.Models;
using Track.Website.Api.v0.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

/// <summary>
/// The single pricing configuration that turns utilization into dollars. Changes take effect live: the next
/// snapshot and any monthly billing query re-price against the current rate.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/v0/billing/rates")]
public class BillingRatesController(
    TrackContext metricsContext) : ControllerBase
{
    protected TrackContext TrackContext { get; } = metricsContext;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var rate = await TrackContext.BillingRates.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? new BillingRate();
        return Ok(ToModel(rate));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] BillingRateModel model, CancellationToken cancellationToken)
    {
        if (model.ClusterComputeMonthlyCost < 0 || model.DiskRatePerGbMonth < 0 || model.ObjectStorageRatePerGbMonth < 0)
            return BadRequest("Rates cannot be negative.");

        var rate = await TrackContext.BillingRates.FirstOrDefaultAsync(cancellationToken);
        if (rate is null)
        {
            rate = new BillingRate();
            TrackContext.BillingRates.Add(rate);
        }

        rate.ClusterComputeMonthlyCost = model.ClusterComputeMonthlyCost;
        rate.DiskRatePerGbMonth = model.DiskRatePerGbMonth;
        rate.ObjectStorageRatePerGbMonth = model.ObjectStorageRatePerGbMonth;
        rate.Currency = string.IsNullOrWhiteSpace(model.Currency) ? "USD" : model.Currency.Trim().ToUpperInvariant();
        rate.UpdatedUtc = DateTime.UtcNow;

        await TrackContext.SaveChangesAsync(cancellationToken);
        return Ok(ToModel(rate));
    }

    private static BillingRateModel ToModel(BillingRate rate) => new()
    {
        ClusterComputeMonthlyCost = rate.ClusterComputeMonthlyCost,
        DiskRatePerGbMonth = rate.DiskRatePerGbMonth,
        ObjectStorageRatePerGbMonth = rate.ObjectStorageRatePerGbMonth,
        Currency = rate.Currency,
    };
}
