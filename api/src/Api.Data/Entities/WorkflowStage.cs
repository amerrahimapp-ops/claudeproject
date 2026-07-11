namespace Api.Data.Entities;

/// <summary>
/// A single stage instance in a specific request's workflow history
/// (as opposed to <see cref="WorkflowConfig"/>, which defines the
/// stage graph itself).
/// </summary>
public class WorkflowStage
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public string StageName { get; set; } = null!;
    public WorkflowStageStatus Status { get; set; }
    public string? AssignedRole { get; set; }

    /// <summary>Optional assignment to a specific user rather than just a role (spec 5.1).</summary>
    public int? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Comments { get; set; }
}
