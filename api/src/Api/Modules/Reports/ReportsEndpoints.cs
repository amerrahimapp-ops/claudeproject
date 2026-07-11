using Api.Data;
using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Reports;

/// <summary>
/// Excel export endpoint (spec Section 9, ADR 0001). Any authenticated user
/// can download a request's report for now — no extra role restriction,
/// matching the Requests/Workflow endpoints' baseline (RequireAuthorization
/// only) since there's no established convention yet for locking this down
/// further.
/// </summary>
public static class ReportsEndpoints
{
    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/requests/{id:int}/report", async (
            int id,
            CapacityDbContext db,
            IReportGenerator reportGenerator) =>
        {
            var request = await db.Requests
                .Include(r => r.WorkflowStages)
                .Include(r => r.RequestorUser)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request is null)
            {
                return Results.NotFound();
            }

            var aiEvaluations = await db.AiEvaluations
                .Where(ae => ae.RequestId == id)
                .OrderByDescending(ae => ae.EvaluatedAt)
                .AsNoTracking()
                .ToListAsync();

            var bytes = reportGenerator.GenerateRequestReport(request, aiEvaluations);

            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{request.RequestNumber}.xlsx");
        })
        .WithName("GetRequestReport")
        .RequireAuthorization();

        return app;
    }
}
