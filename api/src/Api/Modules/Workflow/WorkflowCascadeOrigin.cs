using Api.Data.Entities;

namespace Api.Modules.Workflow;

/// <summary>
/// Carries the *true* starting point of a transition/cascade chain through
/// WorkflowAutomationService's recursive automatic transitions (the
/// submitted -&gt; ai_evaluation -&gt; ai_reviewed chain), so that when the
/// chain finally settles, the notification sent out reports the status the
/// request was actually in before the human's action (e.g. "Draft -&gt;
/// AiReviewed"), not just the last automatic hop's own before/after (which
/// would be an internal detail like "AiEvaluation -&gt; AiReviewed") - and
/// carries the human-authored comment from that original action, not
/// WorkflowAutomationService's internal "Automatic system transition..."
/// placeholder text used on the intermediate hops.
///
/// <see cref="WorkflowEngine.TransitionAsync"/> creates one the first time
/// it's called for a given user action (cascadeOrigin is null) and threads
/// the same instance through every recursive call
/// (WorkflowAutomationService.AdvanceAsync -&gt; engine.TransitionAsync
/// -&gt; ...) until the cascade settles.
/// </summary>
public record WorkflowCascadeOrigin(RequestStatus OldStatus, string? Comments);
