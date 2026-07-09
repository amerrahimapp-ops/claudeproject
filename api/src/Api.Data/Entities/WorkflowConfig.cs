namespace Api.Data.Entities;

/// <summary>
/// Defines the workflow stage graph (as opposed to <see cref="WorkflowStage"/>,
/// which is a per-request stage instance). Seeded at startup for the
/// Phase-1 flow: draft -> submitted -> ai_evaluation -> ai_reviewed ->
/// capacity_review -> infra_approval -> done.
/// </summary>
public class WorkflowConfig
{
    public int Id { get; set; }
    public string StageName { get; set; } = null!;
    public int SequenceOrder { get; set; }

    /// <summary>JSON array of stage names this stage can transition to.</summary>
    public string AllowedTransitions { get; set; } = null!;

    public string? RequiredRole { get; set; }

    /// <summary>JSON blob of additional validation rules for this stage, if any.</summary>
    public string? ValidationRules { get; set; }

    public string? NotificationTemplateId { get; set; }
}
