namespace Track.Website.Api.v0.Models.Stacks;

public class StackModel
{
    public required string StackName { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int ContainerCount { get; set; }
    public bool IsActive { get; set; }
}
