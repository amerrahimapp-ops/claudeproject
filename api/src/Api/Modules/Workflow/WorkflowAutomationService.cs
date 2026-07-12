using Api.Data;
using Api.Data.Entities;
using Api.Modules.Ai;
using Api.Modules.Notifications;
using Api.Modules.Reports;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Workflow;

public interface IWorkflowAutomationService
{
    /// <summary>
    /// Runs whatever "Auto | System" step (spec 7.3) follows a successful
    /// transition to <paramref name="targetStage"/>, if any, and returns the
    /// result that should ultimately be handed back to the original caller
    /// (reflecting the request's *final* state after any further automatic
    /// transitions, not just the one that was directly requested).
    /// <paramref name="origin"/> is this cascade's true starting status/comment
    /// (see <see cref="WorkflowCascadeOrigin"/>) - used to notify the
    /// requestor/next-role exactly once, when the cascade actually settles,
    /// rather than once per intermediate automatic hop.
    /// </summary>
    Task<WorkflowTransitionResult> RunPostTransitionHooksAsync(
        IWorkflowEngine engine,
        Request request,
        string targetStage,
        int actingUserId,
        UserRole actingUserRole,
        WorkflowTransitionResult currentResult,
        WorkflowCascadeOrigin origin,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Centralizes the workflow's System-triggered automations so
/// <see cref="WorkflowEngine.TransitionAsync"/> itself stays focused on the
/// state-machine mechanics (validation, stage bookkeeping, audit log) rather
/// than growing a branch per integration. WorkflowEngine calls this once,
/// right after a transition's own SaveChangesAsync commits, passing itself
/// (as <see cref="IWorkflowEngine"/>) so this service can recurse into
/// further transitions without a circular DI dependency — constructor
/// injection here would create a WorkflowEngine -&gt; IWorkflowAutomationService
/// -&gt; IWorkflowEngine cycle that the container can't resolve; passing the
/// instance as a plain method argument sidesteps that entirely.
///
/// Handles, per spec 7.3:
///   - submitted -&gt; auto-advance into ai_evaluation.
///   - ai_evaluation -&gt; call the AI client (fed with real request data +
///     Grafana utilization, not placeholders), persist the AiEvaluation row,
///     then auto-advance to ai_reviewed on success. On failure, stay at
///     ai_evaluation with the error logged (loosely matches spec 15's "AI
///     model fails -&gt; manual skip"; no "Skip AI" UI is built - out of
///     scope for this task) rather than leaving the request stuck at
///     "submitted" with no trace of what happened.
///   - done -&gt; generate the Excel report and persist it as an Attachment
///     (the on-demand GET .../report endpoint is untouched and keeps
///     regenerating fresh on request; this is an additional side effect).
///
/// Also owns dispatching the Phase 8b workflow notifications (spec 4.4/
/// 10.5) once a transition (or cascade of transitions) truly settles: an
/// email to the request's owner noting the new status, and - if the settled
/// stage's WorkflowConfig has a RequiredRole - an email to every user with
/// that role noting a new task is waiting. "Settles" deliberately excludes
/// the submitted/ai_evaluation-success hops of the automatic cascade (see
/// the switch below and phase-8b-status.md) so one Submit click fires one
/// email, not three.
/// </summary>
public class WorkflowAutomationService : IWorkflowAutomationService
{
    private const string AutoTransitionComments = "Automatic system transition (see WorkflowAutomationService).";

    private readonly IRequestAiEvaluationService _aiEvaluationService;
    private readonly IReportGenerator _reportGenerator;
    private readonly INotificationService _notificationService;
    private readonly CapacityDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<WorkflowAutomationService> _logger;

    public WorkflowAutomationService(
        IRequestAiEvaluationService aiEvaluationService,
        IReportGenerator reportGenerator,
        INotificationService notificationService,
        CapacityDbContext db,
        IWebHostEnvironment environment,
        ILogger<WorkflowAutomationService> logger)
    {
        _aiEvaluationService = aiEvaluationService;
        _reportGenerator = reportGenerator;
        _notificationService = notificationService;
        _db = db;
        _environment = environment;
        _logger = logger;
    }

    public async Task<WorkflowTransitionResult> RunPostTransitionHooksAsync(
        IWorkflowEngine engine,
        Request request,
        string targetStage,
        int actingUserId,
        UserRole actingUserRole,
        WorkflowTransitionResult currentResult,
        WorkflowCascadeOrigin origin,
        CancellationToken cancellationToken = default)
    {
        switch (targetStage)
        {
            case "submitted":
                // Always cascades straight into ai_evaluation within the
                // same call - never a settled state, so no notification here.
                return await AdvanceAsync(engine, request.Id, "ai_evaluation", actingUserId, actingUserRole, currentResult, origin, cancellationToken);

            case "ai_evaluation":
                var evalResult = await _aiEvaluationService.EvaluateAndPersistAsync(request, cancellationToken);
                if (!evalResult.Success)
                {
                    _logger.LogError(
                        "AI evaluation failed for request {RequestId}; leaving it at ai_evaluation for manual " +
                        "follow-up rather than silently losing the request: {Error}",
                        request.Id, evalResult.ErrorMessage);
                    // Unlike the success path, this does NOT cascade further -
                    // the request is genuinely stuck here, which is exactly
                    // the kind of real state change the requestor should
                    // hear about.
                    await NotifySettledTransitionAsync(request, targetStage, origin, cancellationToken);
                    return currentResult;
                }

                return await AdvanceAsync(engine, request.Id, "ai_reviewed", actingUserId, actingUserRole, currentResult, origin, cancellationToken);

            case "done":
                await GenerateAndStoreReportAsync(request, actingUserId, cancellationToken);
                await NotifySettledTransitionAsync(request, targetStage, origin, cancellationToken);
                return currentResult;

            default:
                // Every other reachable target (ai_reviewed, capacity_review,
                // infra_approval, rejected, deferred) has no further
                // automation queued behind it, so it's always a settled state.
                await NotifySettledTransitionAsync(request, targetStage, origin, cancellationToken);
                return currentResult;
        }
    }

    /// <summary>
    /// Notifies the request's owner that the cascade settled at
    /// <paramref name="settledStage"/> (reporting <paramref name="origin"/>'s
    /// pre-cascade status/comment, not this hop's own), and - if
    /// <paramref name="settledStage"/>'s WorkflowConfig names a RequiredRole -
    /// notifies every user with that role that a new task is waiting.
    /// </summary>
    private async Task NotifySettledTransitionAsync(
        Request request, string settledStage, WorkflowCascadeOrigin origin, CancellationToken cancellationToken)
    {
        var newStatus = settledStage.ToRequestStatus();
        await _notificationService.NotifyRequestStatusChangedAsync(
            request, origin.OldStatus, newStatus, origin.Comments, cancellationToken);

        var settledConfig = await _db.WorkflowConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StageName == settledStage, cancellationToken);

        if (settledConfig?.RequiredRole is not null)
        {
            await _notificationService.NotifyRoleOfNewTaskAsync(
                request, settledStage, settledConfig.RequiredRole, origin.Comments, cancellationToken);
        }
    }

    private async Task<WorkflowTransitionResult> AdvanceAsync(
        IWorkflowEngine engine,
        int requestId,
        string nextStage,
        int actingUserId,
        UserRole actingUserRole,
        WorkflowTransitionResult fallback,
        WorkflowCascadeOrigin origin,
        CancellationToken cancellationToken)
    {
        var result = await engine.TransitionAsync(requestId, nextStage, actingUserId, actingUserRole, AutoTransitionComments, origin);
        if (result.Outcome != WorkflowTransitionOutcome.Success)
        {
            // Should not happen in practice (workflow_config allows every
            // stage in this chain and the acting user was already
            // authorized for the prior step) but fail safe rather than
            // throw - return whatever the caller already had.
            _logger.LogWarning(
                "Automatic transition of request {RequestId} to {Stage} did not succeed: {Reason}",
                requestId, nextStage, result.FailureReason);
            return fallback;
        }

        return result;
    }

    private async Task GenerateAndStoreReportAsync(Request request, int actingUserId, CancellationToken cancellationToken)
    {
        try
        {
            var aiEvaluations = await _db.AiEvaluations
                .Where(ae => ae.RequestId == request.Id)
                .OrderByDescending(ae => ae.EvaluatedAt)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var bytes = _reportGenerator.GenerateRequestReport(request, aiEvaluations);

            // Plain filesystem storage under the API's content root - this
            // repo has no blob storage and Phase 1 scale doesn't warrant one
            // (see docs/progress/phase-7b-status.md).
            var directory = Path.Combine(_environment.ContentRootPath, "generated-reports");
            Directory.CreateDirectory(directory);

            var fileName = $"{request.RequestNumber}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.xlsx";
            var fullPath = Path.Combine(directory, fileName);
            await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

            _db.Attachments.Add(new Attachment
            {
                RequestId = request.Id,
                FileName = fileName,
                StoragePath = fullPath,
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                UploadedByUserId = actingUserId,
                UploadedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Don't let report generation/storage failures fail the "done"
            // transition itself - the request has legitimately completed its
            // workflow; the report is a side effect, and the on-demand
            // GET .../report endpoint remains available as a fallback.
            _logger.LogError(ex, "Failed to auto-generate/store the Excel report for request {RequestId} at done", request.Id);
        }
    }
}
