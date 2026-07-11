using Track.Data;
using Track.Data.Models;
using Track.Website.Api.v0.Models.Buckets;
using Track.Website.Api.v0.Models.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Api.v0.Controllers;

[ApiController]
[Authorize]
[Route("/api/v0/customers")]
public class CustomersController(
    ILogger<CustomersController> logger,
    TrackContext metricsContext
) : ControllerBase
{
    protected ILogger<CustomersController> Logger { get; } = logger;
    protected TrackContext TrackContext { get; } = metricsContext;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var customers = await TrackContext.Customers
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CustomerModel
            {
                Id = c.Id,
                Name = c.Name,
                Notes = c.Notes,
                CreatedAt = c.CreatedAt,
                StackNames = c.StackAssignments.Select(a => a.StackName).OrderBy(n => n).ToList(),
                Buckets = c.BucketAssignments
                    .OrderBy(a => a.SourceName).ThenBy(a => a.BucketName)
                    .Select(a => new BucketRefModel { SourceName = a.SourceName, BucketName = a.BucketName })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return Ok(customers);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerEditModel model, CancellationToken cancellationToken)
    {
        var name = model.Name.Trim();
        if (name.Length == 0)
            return BadRequest("Name is required.");

        if (await TrackContext.Customers.AnyAsync(c => c.Name == name, cancellationToken))
            return Conflict("A customer with that name already exists.");

        var customer = new Customer
        {
            Name = name,
            Notes = model.Notes,
            CreatedAt = DateTime.UtcNow,
        };
        TrackContext.Customers.Add(customer);
        await TrackContext.SaveChangesAsync(cancellationToken);

        return Ok(new CustomerModel
        {
            Id = customer.Id,
            Name = customer.Name,
            Notes = customer.Notes,
            CreatedAt = customer.CreatedAt,
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        [FromRoute] int id, [FromBody] CustomerEditModel model, CancellationToken cancellationToken)
    {
        var customer = await TrackContext.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
            return NotFound();

        var name = model.Name.Trim();
        if (name.Length == 0)
            return BadRequest("Name is required.");

        if (await TrackContext.Customers.AnyAsync(c => c.Id != id && c.Name == name, cancellationToken))
            return Conflict("A customer with that name already exists.");

        customer.Name = name;
        customer.Notes = model.Notes;
        await TrackContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        var customer = await TrackContext.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
            return NotFound();

        TrackContext.Customers.Remove(customer);
        await TrackContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }

    /// <summary>Replaces the customer's assigned stacks; stacks already assigned elsewhere are moved here.</summary>
    [HttpPut("{id:int}/stacks")]
    public async Task<IActionResult> SetStacks(
        [FromRoute] int id, [FromBody] List<string> stackNames, CancellationToken cancellationToken)
    {
        var customer = await TrackContext.Customers
            .Include(c => c.StackAssignments)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
            return NotFound();

        var names = stackNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = await TrackContext.StackAssignments
            .Where(a => names.Contains(a.StackName))
            .ToListAsync(cancellationToken);
        foreach (var assignment in existing)
            assignment.CustomerId = id;

        var existingNames = existing.Select(a => a.StackName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        TrackContext.StackAssignments.AddRange(names
            .Where(n => !existingNames.Contains(n))
            .Select(n => new StackAssignment { StackName = n, CustomerId = id }));

        TrackContext.StackAssignments.RemoveRange(customer.StackAssignments
            .Where(a => !names.Contains(a.StackName, StringComparer.OrdinalIgnoreCase)));

        await TrackContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }

    /// <summary>Replaces the customer's assigned buckets; buckets already assigned elsewhere are moved here.</summary>
    [HttpPut("{id:int}/buckets")]
    public async Task<IActionResult> SetBuckets(
        [FromRoute] int id, [FromBody] List<BucketRefModel> buckets, CancellationToken cancellationToken)
    {
        var customer = await TrackContext.Customers
            .Include(c => c.BucketAssignments)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
            return NotFound();

        var refs = buckets
            .Select(b => (Source: b.SourceName.Trim(), Bucket: b.BucketName.Trim()))
            .Where(b => b.Source.Length > 0 && b.Bucket.Length > 0)
            .Distinct()
            .ToList();

        // EF Core can't translate a tuple Contains, so match on the source set and filter names in memory.
        var sources = refs.Select(b => b.Source).Distinct().ToList();
        var candidates = await TrackContext.BucketAssignments
            .Where(a => sources.Contains(a.SourceName))
            .ToListAsync(cancellationToken);
        var existing = candidates
            .Where(a => refs.Any(r =>
                string.Equals(r.Source, a.SourceName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Bucket, a.BucketName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (var assignment in existing)
            assignment.CustomerId = id;

        bool AlreadyAssigned(string source, string bucket) => existing.Any(a =>
            string.Equals(a.SourceName, source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.BucketName, bucket, StringComparison.OrdinalIgnoreCase));

        TrackContext.BucketAssignments.AddRange(refs
            .Where(r => !AlreadyAssigned(r.Source, r.Bucket))
            .Select(r => new BucketAssignment { SourceName = r.Source, BucketName = r.Bucket, CustomerId = id }));

        bool Retained(BucketAssignment a) => refs.Any(r =>
            string.Equals(r.Source, a.SourceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Bucket, a.BucketName, StringComparison.OrdinalIgnoreCase));

        TrackContext.BucketAssignments.RemoveRange(customer.BucketAssignments.Where(a => !Retained(a)));

        await TrackContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }
}
