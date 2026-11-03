namespace Track.Website.Api.v0.Models.Stacks;

/// <summary>Where one stack belongs: a customer, shared infrastructure, or neither (unassigned).</summary>
public class StackAssignmentEditModel
{
    public int? CustomerId { get; set; }

    /// <summary>Ignored when <see cref="CustomerId"/> is set.</summary>
    public bool IsShared { get; set; }
}
