namespace Track.Website.Api.v0.Models.Customers;

public class CustomerModel
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> StackNames { get; set; } = [];
}
