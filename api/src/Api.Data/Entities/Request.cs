namespace Api.Data.Entities;

public class Request
{
    public int Id { get; set; }

    /// <summary>Format CAP-YYYY-NNNN, unique.</summary>
    public string RequestNumber { get; set; } = null!;

    public RequestStatus Status { get; set; }
    public RequestEnvironment Environment { get; set; }
    public ProjectType ProjectType { get; set; }
    public RequestPriority Priority { get; set; }

    /// <summary>JSON blob describing current capacity figures.</summary>
    public string? CurrentCapacity { get; set; }

    /// <summary>JSON blob describing requested capacity figures.</summary>
    public string? RequestedCapacity { get; set; }

    /// <summary>JSON blob describing uplift percentages per resource type.</summary>
    public string? UpliftPercentages { get; set; }

    public int RequestorUserId { get; set; }
    public User RequestorUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token, manually incremented on every workflow
    /// transition (see WorkflowEngine). Prevents two concurrent transitions
    /// on the same request from both succeeding and silently overwriting
    /// each other's state.
    /// </summary>
    public int ConcurrencyVersion { get; set; }

    public ICollection<RequestServer> RequestServers { get; set; } = new List<RequestServer>();
    public ICollection<Justification> Justifications { get; set; } = new List<Justification>();
    public ICollection<WorkflowStage> WorkflowStages { get; set; } = new List<WorkflowStage>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
