using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Workflow;

public interface IWorkflowEngine
{
    /// <summary>
    /// <paramref name="cascadeOrigin"/> is internal plumbing: leave it null
    /// for every real caller (HTTP endpoint, tests). WorkflowAutomationService
    /// supplies it when recursively driving an automatic cascade so the
    /// eventual settled-stage notification reports the cascade's true
    /// starting status/comment rather than an intermediate hop's own.
    /// </summary>
    Task<WorkflowTransitionResult> TransitionAsync(
        int requestId, string targetStage, int actingUserId, UserRole actingUserRole, string? comments,
        WorkflowCascadeOrigin? cascadeOrigin = null);
}

/// <summary>
/// The Phase-1 workflow state machine. Every transition is validated against
/// the seeded <see cref="WorkflowConfig"/> graph (allowed-transitions +
/// required-role) and, on success, atomically updates the request's status,
/// closes out the current <see cref="WorkflowStage"/> row, opens the next
/// one, and writes an <see cref="AuditLog"/> entry. On rejection nothing is
/// staged, so no partial state can leak out (fail closed).
/// </summary>
public class WorkflowEngine : IWorkflowEngine
{
    private static readonly string[] TerminalStages = ["done", "rejected", "deferred"];

    private readonly CapacityDbContext _db;
    private readonly IWorkflowAutomationService _automation;

    public WorkflowEngine(CapacityDbContext db, IWorkflowAutomationService automation)
    {
        _db = db;
        _automation = automation;
    }

    public async Task<WorkflowTransitionResult> TransitionAsync(
        int requestId, string targetStage, int actingUserId, UserRole actingUserRole, string? comments,
        WorkflowCascadeOrigin? cascadeOrigin = null)
    {
        var request = await _db.Requests
            .Include(r => r.RequestorUser)
            .Include(r => r.RequestServers)
            .Include(r => r.Justifications)
            .Include(r => r.WorkflowStages)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request is null)
        {
            return WorkflowTransitionResult.NotFound();
        }

        var currentStageName = request.Status.ToStageName();

        var currentConfig = await _db.WorkflowConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StageName == currentStageName);

        // No config row means the request is sitting in a terminal state
        // (done / rejected / deferred are valid transition TARGETS but never
        // have a WorkflowConfig row of their own to transition FROM).
        if (currentConfig is null)
        {
            return WorkflowTransitionResult.Illegal(
                $"Request {requestId} is in terminal stage \"{currentStageName}\"; no further transitions are allowed.");
        }

        var allowedTransitions = JsonSerializer.Deserialize<string[]>(currentConfig.AllowedTransitions)
            ?? [];
        if (!allowedTransitions.Contains(targetStage))
        {
            return WorkflowTransitionResult.Illegal(
                $"Transition from \"{currentStageName}\" to \"{targetStage}\" is not allowed.");
        }

        if (!targetStage.TryToRequestStatus(out var targetStatus))
        {
            return WorkflowTransitionResult.Illegal($"\"{targetStage}\" is not a recognized workflow stage.");
        }

        // RequiredRole == null covers stages with no specific approver role
        // (draft, and the system-driven AI stages once Phase 4 wires the
        // real adapter in). That must NOT mean "any authenticated user" —
        // it means "the request's own owner, or an Admin". Without this,
        // any authenticated user could push someone else's draft forward.
        if (currentConfig.RequiredRole is not null)
        {
            if (!string.Equals(currentConfig.RequiredRole, actingUserRole.ToString(), StringComparison.Ordinal))
            {
                return WorkflowTransitionResult.WrongRole(
                    $"Transitioning from \"{currentStageName}\" requires role \"{currentConfig.RequiredRole}\"; " +
                    $"acting user has role \"{actingUserRole}\".");
            }
        }
        else if (actingUserRole != UserRole.Admin && actingUserId != request.RequestorUserId)
        {
            return WorkflowTransitionResult.WrongRole(
                $"Transitioning from \"{currentStageName}\" requires no specific role, but only the " +
                "request's owner or an Admin may perform it.");
        }

        var now = DateTime.UtcNow;
        var isTerminalTarget = TerminalStages.Contains(targetStage);

        // Close out the current stage's WorkflowStage row. "draft" never has
        // one (no WorkflowStage row is created at request-creation time), so
        // create it implicitly here rather than erroring.
        var currentStageRow = request.WorkflowStages
            .Where(ws => ws.StageName == currentStageName && ws.Status == WorkflowStageStatus.InProgress)
            .OrderByDescending(ws => ws.StartedAt)
            .FirstOrDefault();

        if (currentStageRow is null)
        {
            currentStageRow = new WorkflowStage
            {
                RequestId = request.Id,
                StageName = currentStageName,
                AssignedRole = currentConfig.RequiredRole,
                StartedAt = request.CreatedAt,
            };
            _db.WorkflowStages.Add(currentStageRow);
        }

        currentStageRow.Status = targetStage switch
        {
            "rejected" => WorkflowStageStatus.Rejected,
            "deferred" => WorkflowStageStatus.Deferred,
            _ => WorkflowStageStatus.Approved,
        };
        currentStageRow.CompletedAt = now;
        if (!string.IsNullOrWhiteSpace(comments))
        {
            currentStageRow.Comments = comments;
        }

        // Open the new stage's WorkflowStage row. Terminal targets (done /
        // rejected / deferred) close out immediately rather than being left
        // dangling InProgress.
        var targetConfig = await _db.WorkflowConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StageName == targetStage);

        var newStageRow = new WorkflowStage
        {
            RequestId = request.Id,
            StageName = targetStage,
            AssignedRole = targetConfig?.RequiredRole,
            StartedAt = now,
        };

        if (isTerminalTarget)
        {
            newStageRow.Status = targetStage switch
            {
                "rejected" => WorkflowStageStatus.Rejected,
                "deferred" => WorkflowStageStatus.Deferred,
                _ => WorkflowStageStatus.Approved, // "done"
            };
            newStageRow.CompletedAt = now;
        }
        else
        {
            newStageRow.Status = WorkflowStageStatus.InProgress;
        }

        _db.WorkflowStages.Add(newStageRow);

        var oldStatus = request.Status;
        request.Status = targetStatus;
        request.UpdatedAt = now;
        // Manually-incremented optimistic concurrency token: EF Core includes
        // the pre-increment value in the UPDATE's WHERE clause, so if another
        // transition already committed against this request since we read it,
        // zero rows match and SaveChangesAsync throws DbUpdateConcurrencyException
        // below instead of silently overwriting the other transition's result.
        request.ConcurrencyVersion++;

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Request",
            EntityId = request.Id,
            Action = "WorkflowTransition",
            OldValues = JsonSerializer.Serialize(new { status = oldStatus.ToString() }),
            NewValues = JsonSerializer.Serialize(new { status = targetStatus.ToString() }),
            PerformedByUserId = actingUserId,
            PerformedAt = now,
        });

        // Single SaveChangesAsync call: EF Core wraps it in one implicit
        // transaction, so all of the above either commits together or (on
        // failure) none of it does — no partial state.
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return WorkflowTransitionResult.Illegal(
                $"Request {requestId} was modified by another transition concurrently; please retry.");
        }

        var result = WorkflowTransitionResult.Success(request);

        // The first time this method is entered for a given user action,
        // cascadeOrigin is null - capture THIS hop's own pre-transition
        // status/comment as the cascade's origin. Recursive calls made by
        // WorkflowAutomationService.AdvanceAsync pass the same origin back
        // in, so it survives unchanged through however many automatic hops
        // follow (see WorkflowCascadeOrigin).
        var origin = cascadeOrigin ?? new WorkflowCascadeOrigin(oldStatus, comments);

        // System-triggered continuations (spec 7.3's "Auto | System" steps —
        // ai evaluation chain, Excel-on-done) live in a dedicated service
        // rather than inline here, so this method stays focused on the
        // state-machine mechanics. See WorkflowAutomationService for why it
        // takes `this` as a parameter instead of being constructor-injected.
        // That same service also owns notifying the requestor/next-role
        // once a cascade settles, using `origin` to report the true
        // before/after rather than an intermediate hop's own.
        return await _automation.RunPostTransitionHooksAsync(
            this, request, targetStage, actingUserId, actingUserRole, result, origin);
    }
}
