using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Admin;

public record AuditLogEntryResponse(
    int Id,
    string EntityType,
    int EntityId,
    string Action,
    string PerformedByUserName,
    DateTime PerformedAt,
    string? OldValues,
    string? NewValues);

public record AuditLogPageResponse(
    IReadOnlyList<AuditLogEntryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// Admin-only audit log viewer. Reads the AuditLogs table (see
/// api/src/Api.Data/Entities/AuditLog.cs) which today is written to by
/// exactly one place: WorkflowEngine.TransitionAsync logs one row per
/// workflow stage transition ("Request" / "WorkflowTransition"). No other
/// entity type currently writes rows — see docs/progress/phase-6a-status.md
/// for the gap this leaves (e.g. no audit trail for user or workflow_config
/// admin actions, none of which exist as mutable endpoints yet either).
/// Paginated, newest first, with optional entityType/entityId/action/
/// performedByUserId filters. Joins to User for a display name so callers
/// don't have to resolve PerformedByUserId themselves.
/// </summary>
public static class AuditLogEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/audit-log", async (
            CapacityDbContext db,
            int? page,
            int? pageSize,
            string? entityType,
            int? entityId,
            string? action,
            int? performedByUserId) =>
        {
            var effectivePage = page is > 0 ? page.Value : 1;
            var effectivePageSize = pageSize is > 0 ? Math.Min(pageSize.Value, MaxPageSize) : DefaultPageSize;

            var query = db.AuditLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(entityType))
            {
                query = query.Where(a => a.EntityType == entityType);
            }

            if (entityId.HasValue)
            {
                query = query.Where(a => a.EntityId == entityId.Value);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(a => a.Action == action);
            }

            if (performedByUserId.HasValue)
            {
                query = query.Where(a => a.PerformedByUserId == performedByUserId.Value);
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(a => a.PerformedAt)
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .Select(a => new AuditLogEntryResponse(
                    a.Id,
                    a.EntityType,
                    a.EntityId,
                    a.Action,
                    a.PerformedByUser.DisplayName,
                    a.PerformedAt,
                    a.OldValues,
                    a.NewValues))
                .ToListAsync();

            return Results.Ok(new AuditLogPageResponse(items, effectivePage, effectivePageSize, totalCount));
        })
        .WithName("GetAuditLog")
        .RequireAuthorization(policy => policy.RequireRole("Admin"));

        return app;
    }
}
