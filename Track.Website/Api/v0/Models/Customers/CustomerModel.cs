using Track.Website.Api.v0.Models.Buckets;

namespace Track.Website.Api.v0.Models.Customers;

public class CustomerModel
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> StackNames { get; set; } = [];
    public List<BucketRefModel> Buckets { get; set; } = [];
}
