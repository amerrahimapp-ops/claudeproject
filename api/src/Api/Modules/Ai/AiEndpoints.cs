using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Ai;

public record TestAiEvaluationRequest(int RequestId, string? UtilizationMetricsJson);

/// <summary>
/// Diagnostic endpoint for verifying the configured AI provider actually
/// works - not part of the product feature set on its own (the real
/// workflow integration point, wiring this into the ai_evaluation stage
/// transition, is a later phase). Admin-only. Every call is logged to
/// ai_evaluations regardless of success/failure (ADR 0002 - full audit
/// trail of prompt+response for an approval-affecting decision).
/// </summary>
public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/admin/test-ai-evaluation", async (
            TestAiEvaluationRequest body, IAiEvaluationClient aiClient, CapacityDbContext db) =>
        {
            var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == body.RequestId);
            if (request is null)
            {
                return Results.NotFound();
            }

            var summaryJson = JsonSerializer.Serialize(new
            {
                request.RequestNumber,
                Status = request.Status.ToString(),
                Environment = request.Environment.ToString(),
                ProjectType = request.ProjectType.ToString(),
                Priority = request.Priority.ToString(),
            });
            var metricsJson = body.UtilizationMetricsJson ?? "{}";

            var result = await aiClient.EvaluateAsync(new AiEvaluationRequest(request.Id, summaryJson, metricsJson));

            db.AiEvaluations.Add(new AiEvaluation
            {
                RequestId = request.Id,
                Prompt = result.Prompt,
                RawResponse = result.RawResponse,
                Score = result.Response?.Score,
                Recommendation = result.Response?.Recommendation,
                FlagsJson = result.Response is null ? null : JsonSerializer.Serialize(result.Response.Flags),
                EvaluatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            return result.Success
                ? Results.Ok(new { success = true, result.Response })
                : Results.Json(new { success = false, error = result.ErrorMessage }, statusCode: 502);
        })
        .WithName("TestAiEvaluation")
        .RequireAuthorization(policy => policy.RequireRole("Admin"));

        return app;
    }
}
