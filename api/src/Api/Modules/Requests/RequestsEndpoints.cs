using System.Security.Claims;
using Api.Data;
using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Requests;

/// <summary>
/// Minimal Requests substrate for Phase 3 (create + get-by-id only) so the
/// workflow engine has something real to operate on. The full 5-step
/// wizard, justifications, servers, and attachments land in a later phase.
/// </summary>
public static class RequestsEndpoints
{
    public static IEndpointRouteBuilder MapRequestsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/requests", async (
            CreateRequestRequest body,
            ClaimsPrincipal user,
            CapacityDbContext db) =>
        {
            if (!Enum.TryParse<RequestEnvironment>(body.Environment, ignoreCase: true, out var environment))
            {
                return Results.BadRequest(new { error = $"Unknown environment \"{body.Environment}\"." });
            }

            if (!Enum.TryParse<ProjectType>(body.ProjectType, ignoreCase: true, out var projectType))
            {
                return Results.BadRequest(new { error = $"Unknown projectType \"{body.ProjectType}\"." });
            }

            if (!Enum.TryParse<RequestPriority>(body.Priority, ignoreCase: true, out var priority))
            {
                return Results.BadRequest(new { error = $"Unknown priority \"{body.Priority}\"." });
            }

            var actingUserId = int.Parse(user.FindFirstValue("user_id")!);
            var now = DateTime.UtcNow;
            var year = now.Year;

            // Simplest correct approach for now: count existing requests for the
            // current year and add 1. Two concurrent requests in the same year
            // can race and read the same count before either commits, producing
            // a duplicate RequestNumber — acceptable to accept for Phase 3.
            var countThisYear = await db.Requests.CountAsync(r => r.RequestNumber.StartsWith($"CAP-{year}-"));
            var requestNumber = $"CAP-{year}-{countThisYear + 1:D4}";

            var request = new Request
            {
                RequestNumber = requestNumber,
                Status = RequestStatus.Draft,
                Environment = environment,
                ProjectType = projectType,
                Priority = priority,
                RequestorUserId = actingUserId,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Requests.Add(request);
            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/requests/{request.Id}", RequestMapper.ToResponse(request));
        })
        .WithName("CreateRequest")
        .RequireAuthorization();

        app.MapGet("/api/v1/requests/{id:int}", async (int id, CapacityDbContext db) =>
        {
            var request = await db.Requests
                .Include(r => r.WorkflowStages)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);

            return request is null
                ? Results.NotFound()
                : Results.Ok(RequestMapper.ToResponse(request));
        })
        .WithName("GetRequestById")
        .RequireAuthorization();

        return app;
    }
}
