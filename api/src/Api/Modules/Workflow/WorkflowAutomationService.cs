using Api.Data;
using Api.Data.Entities;
using Api.Modules.Ai;
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
    /// </summary>
    Task<WorkflowTransitionResult> RunPostTransitionHooksAsync(
        IWorkflowEngine engine,
        Request request,
        string targetStage,
        int actingUserId,
        UserRole actingUserRole,
        WorkflowTransitionResult currentResult,
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
/// </summary>
public class WorkflowAutomationService : IWorkflowAutomationService
{
    private const string AutoTransitionComments = "Automatic system transition (see WorkflowAutomationService).";

    private readonly IRequestAiEvaluationService _aiEvaluationService;
    private readonly IReportGenerator _reportGenerator;
    private readonly CapacityDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<WorkflowAutomationService> _logger;

    public WorkflowAutomationService(
        IRequestAiEvaluationService aiEvaluationService,
        IReportGenerator reportGenerator,
        CapacityDbContext db,
        IWebHostEnvironment environment,
        ILogger<WorkflowAutomationService> logger)
    {
        _aiEvaluationService = aiEvaluationService;
        _reportGenerator = reportGenerator;
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
        CancellationToken cancellationToken = default)
    {
        switch (targetStage)
        {
            case "submitted":
                return await AdvanceAsync(engine, request.Id, "ai_evaluation", actingUserId, actingUserRole, currentResult, cancellationToken);

            case "ai_evaluation":
                var evalResult = await _aiEvaluationService.EvaluateAndPersistAsync(request, cancellationToken);
                if (!evalResult.Success)
                {
                    _logger.LogError(
                        "AI evaluation failed for request {RequestId}; leaving it at ai_evaluation for manual " +
                        "follow-up rather than silently losing the request: {Error}",
                        request.Id, evalResult.ErrorMessage);
                    return currentResult;
                }

                return await AdvanceAsync(engine, request.Id, "ai_reviewed", actingUserId, actingUserRole, currentResult, cancellationToken);

            case "done":
                await GenerateAndStoreReportAsync(request, actingUserId, cancellationToken);
                return currentResult;

            default:
                return currentResult;
        }
    }

    private async Task<WorkflowTransitionResult> AdvanceAsync(
        IWorkflowEngine engine,
        int requestId,
        string nextStage,
        int actingUserId,
        UserRole actingUserRole,
        WorkflowTransitionResult fallback,
        CancellationToken cancellationToken)
    {
        var result = await engine.TransitionAsync(requestId, nextStage, actingUserId, actingUserRole, AutoTransitionComments);
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
