using System.Text.Json;
using Api.Data;
using Api.Modules.Integrations.Grafana;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Ai;

// ---------------------------------------------------------------------
// Response shapes for GET /api/v1/requests/{id}/ai-insights (Phase 7b).
// Phase 7c's "AI Insights" panel on Request Detail builds directly against
// this shape - see docs/progress/phase-7b-status.md for the documented
// contract.
// ---------------------------------------------------------------------

public record MetricStatsResponse(double? Avg, double? Max, double? P95);

public record ServerUtilizationResponse(
    string Hostname,
    bool Success,
    string? ErrorMessage,
    MetricStatsResponse? Cpu,
    MetricStatsResponse? Memory,
    MetricStatsResponse? Disk);

public record LatestAiEvaluationResponse(
    int Id,
    DateTime EvaluatedAt,
    double? Score,
    string? Recommendation,
    IReadOnlyList<string> Flags);

public record AiInsightsResponse(
    LatestAiEvaluationResponse? LatestEvaluation,
    IReadOnlyList<ServerUtilizationResponse> ServerUtilization);

/// <summary>
/// Read-only endpoint backing the "AI Insights" panel (spec 8.3). Any
/// authenticated user, standard .RequireAuthorization() — same baseline as
/// the Requests/Workflow endpoints, no extra role restriction.
/// </summary>
public static class AiInsightsEndpoints
{
    public static IEndpointRouteBuilder MapAiInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/requests/{id:int}/ai-insights", async (
            int id, CapacityDbContext db, IGrafanaUtilizationService utilizationService) =>
        {
            var request = await db.Requests
                .Include(r => r.RequestServers)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request is null)
            {
                return Results.NotFound();
            }

            var latestEvaluation = await db.AiEvaluations
                .Where(ae => ae.RequestId == id)
                .OrderByDescending(ae => ae.EvaluatedAt)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            LatestAiEvaluationResponse? evaluationResponse = latestEvaluation is null
                ? null
                : new LatestAiEvaluationResponse(
                    latestEvaluation.Id,
                    latestEvaluation.EvaluatedAt,
                    latestEvaluation.Score,
                    latestEvaluation.Recommendation,
                    string.IsNullOrWhiteSpace(latestEvaluation.FlagsJson)
                        ? []
                        : JsonSerializer.Deserialize<string[]>(latestEvaluation.FlagsJson) ?? []);

            var hostnames = request.RequestServers
                .Select(s => s.Hostname)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var utilization = hostnames.Count > 0
                ? await utilizationService.GetUtilizationAsync(hostnames)
                : [];

            var utilizationResponse = utilization
                .Select(u => new ServerUtilizationResponse(
                    u.Hostname, u.Success, u.ErrorMessage,
                    ToStatsResponse(u.Cpu), ToStatsResponse(u.Memory), ToStatsResponse(u.Disk)))
                .ToList();

            return Results.Ok(new AiInsightsResponse(evaluationResponse, utilizationResponse));
        })
        .WithName("GetRequestAiInsights")
        .RequireAuthorization();

        return app;
    }

    private static MetricStatsResponse? ToStatsResponse(MetricStats? stats) =>
        stats is null ? null : new MetricStatsResponse(stats.Avg, stats.Max, stats.P95);
}
