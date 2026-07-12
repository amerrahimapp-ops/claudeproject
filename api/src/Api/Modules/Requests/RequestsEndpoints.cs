using System.Security.Claims;
using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Requests;

/// <summary>
/// Requests substrate (create + get-by-id). Phase 7a expanded create to
/// accept the full 5-step wizard payload in one POST (Requestor Info is
/// read-only display data from the JWT/User, not part of the body — see
/// RequestsDtos.cs) — resources, servers, and justifications are all
/// persisted together with the parent Request in a single SaveChangesAsync
/// call (one implicit transaction, all-or-nothing).
/// </summary>
public static class RequestsEndpoints
{
    private static readonly IReadOnlyList<string> ValidResourceTypes =
        Enum.GetNames<ResourceType>();

    private static readonly IReadOnlyList<string> ValidPlatforms =
        Enum.GetNames<Platform>();

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

            var validationError = Validate(body);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var actingUserId = int.Parse(user.FindFirstValue("user_id")!);
            var actingUser = await db.Users.FindAsync(actingUserId);
            if (actingUser is null)
            {
                return Results.BadRequest(new { error = "Acting user not found." });
            }

            var now = DateTime.UtcNow;
            var year = now.Year;

            // Simplest correct approach for now: count existing requests for the
            // current year and add 1. Two concurrent requests in the same year
            // can race and read the same count before either commits, producing
            // a duplicate RequestNumber — acceptable to accept for Phase 3.
            var countThisYear = await db.Requests.CountAsync(r => r.RequestNumber.StartsWith($"CAP-{year}-"));
            var requestNumber = $"CAP-{year}-{countThisYear + 1:D4}";

            // Uplift % is always computed here, server-side, from the
            // current/requested values the caller sent for each resource —
            // never trust a client-supplied percentage.
            var currentCapacity = new Dictionary<string, decimal>();
            var requestedCapacity = new Dictionary<string, decimal>();
            var upliftPercentages = new Dictionary<string, decimal?>();

            foreach (var resource in body.Resources)
            {
                // Normalize to the canonical enum name (e.g. "storage" -> "Storage") so the
                // JSON map keys are consistent regardless of the casing the client sent.
                var resourceType = Enum.Parse<ResourceType>(resource.ResourceType, ignoreCase: true).ToString();
                currentCapacity[resourceType] = resource.CurrentValue;
                requestedCapacity[resourceType] = resource.RequestedValue;
                upliftPercentages[resourceType] = resource.CurrentValue > 0
                    ? Math.Round((resource.RequestedValue - resource.CurrentValue) / resource.CurrentValue * 100, 2)
                    : null;
            }

            var request = new Request
            {
                RequestNumber = requestNumber,
                Status = RequestStatus.Draft,
                Title = body.Title.Trim(),
                Department = body.Department.Trim(),
                ProjectName = body.ProjectName.Trim(),
                ProjectCode = body.ProjectCode.Trim(),
                Sponsor = body.Sponsor.Trim(),
                Environment = environment,
                ProjectType = projectType,
                Priority = priority,
                StartDate = body.StartDate,
                EndDate = body.EndDate,
                Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
                CurrentCapacity = JsonSerializer.Serialize(currentCapacity),
                RequestedCapacity = JsonSerializer.Serialize(requestedCapacity),
                UpliftPercentages = JsonSerializer.Serialize(upliftPercentages),
                RequestorUserId = actingUserId,
                RequestorUser = actingUser,
                CreatedAt = now,
                UpdatedAt = now,
            };

            foreach (var server in body.Servers ?? [])
            {
                request.RequestServers.Add(new RequestServer
                {
                    Hostname = server.Hostname.Trim(),
                    IpAddress = server.IpAddress.Trim(),
                    Os = string.IsNullOrWhiteSpace(server.Os) ? null : server.Os.Trim(),
                    IsPhysical = server.IsPhysical,
                    ResourceType = Enum.Parse<ResourceType>(server.ResourceType, ignoreCase: true),
                    CurrentValue = server.CurrentValue,
                    RequestedValue = server.RequestedValue,
                    MountPoint = string.IsNullOrWhiteSpace(server.MountPoint) ? null : server.MountPoint.Trim(),
                    Platform = Enum.Parse<Platform>(server.Platform, ignoreCase: true),
                    DrApplicable = server.DrApplicable,
                    AppTier = string.IsNullOrWhiteSpace(server.AppTier) ? null : server.AppTier.Trim(),
                });
            }

            foreach (var justification in body.Justifications ?? [])
            {
                request.Justifications.Add(new Justification
                {
                    ResourceType = Enum.Parse<ResourceType>(justification.ResourceType, ignoreCase: true),
                    QuestionKey = justification.QuestionKey.Trim(),
                    AnswerText = justification.AnswerText.Trim(),
                });
            }

            db.Requests.Add(request);
            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/requests/{request.Id}", RequestMapper.ToResponse(request));
        })
        .WithName("CreateRequest")
        .RequireAuthorization();

        app.MapGet("/api/v1/requests/{id:int}", async (int id, CapacityDbContext db) =>
        {
            var request = await db.Requests
                .Include(r => r.RequestorUser)
                .Include(r => r.RequestServers)
                .Include(r => r.Justifications)
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

    /// <summary>
    /// Reasonable validation matching the existing endpoint's BadRequest
    /// style — not exhaustive, just enough to reject obviously-bad input
    /// (empty required text, negative capacity values, backwards date
    /// ranges, unknown resource/platform enum strings, no resources at all).
    /// </summary>
    private static string? Validate(CreateRequestRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Title)) return "title is required.";
        if (string.IsNullOrWhiteSpace(body.Department)) return "department is required.";
        if (string.IsNullOrWhiteSpace(body.ProjectName)) return "projectName is required.";
        if (string.IsNullOrWhiteSpace(body.ProjectCode)) return "projectCode is required.";
        if (string.IsNullOrWhiteSpace(body.Sponsor)) return "sponsor is required.";

        if (body.EndDate < body.StartDate)
        {
            return "endDate must be on or after startDate.";
        }

        if (body.Resources is null || body.Resources.Count == 0)
        {
            return "At least one resource type must be selected.";
        }

        var seenResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in body.Resources)
        {
            if (!ValidResourceTypes.Contains(resource.ResourceType, StringComparer.OrdinalIgnoreCase))
            {
                return $"Unknown resource type \"{resource.ResourceType}\".";
            }

            if (!seenResourceTypes.Add(resource.ResourceType))
            {
                return $"Duplicate resource type \"{resource.ResourceType}\" in resources.";
            }

            if (resource.CurrentValue < 0)
            {
                return $"currentValue for \"{resource.ResourceType}\" must be >= 0.";
            }

            if (resource.RequestedValue < 0)
            {
                return $"requestedValue for \"{resource.ResourceType}\" must be >= 0.";
            }
        }

        foreach (var server in body.Servers ?? [])
        {
            if (string.IsNullOrWhiteSpace(server.Hostname)) return "server hostname is required.";
            if (string.IsNullOrWhiteSpace(server.IpAddress)) return "server ipAddress is required.";

            if (!ValidResourceTypes.Contains(server.ResourceType, StringComparer.OrdinalIgnoreCase))
            {
                return $"Unknown resource type \"{server.ResourceType}\" for server \"{server.Hostname}\".";
            }

            if (!ValidPlatforms.Contains(server.Platform, StringComparer.OrdinalIgnoreCase))
            {
                return $"Unknown platform \"{server.Platform}\" for server \"{server.Hostname}\".";
            }

            if (server.CurrentValue < 0 || server.RequestedValue < 0)
            {
                return $"server \"{server.Hostname}\" current/requested values must be >= 0.";
            }
        }

        foreach (var justification in body.Justifications ?? [])
        {
            if (!ValidResourceTypes.Contains(justification.ResourceType, StringComparer.OrdinalIgnoreCase))
            {
                return $"Unknown resource type \"{justification.ResourceType}\" in justifications.";
            }

            if (string.IsNullOrWhiteSpace(justification.QuestionKey)) return "justification questionKey is required.";
            if (string.IsNullOrWhiteSpace(justification.AnswerText)) return "justification answerText is required.";
        }

        return null;
    }
}
