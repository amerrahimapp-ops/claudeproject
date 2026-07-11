using Api.Data.Entities;

namespace Api.Modules.Requests;

/// <summary>Minimal create-request body for Phase 3 — the full 5-step wizard is a later phase.</summary>
public record CreateRequestRequest(string Environment, string ProjectType, string Priority);

public record WorkflowStageResponse(
    int Id,
    string StageName,
    string Status,
    string? AssignedRole,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Comments);

public record RequestResponse(
    int Id,
    string RequestNumber,
    string Status,
    string Environment,
    string ProjectType,
    string Priority,
    int RequestorUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<WorkflowStageResponse> WorkflowStages);

/// <summary>Single place that maps the Request entity graph to its API shape (also used by the Workflow module).</summary>
public static class RequestMapper
{
    public static RequestResponse ToResponse(Request request) => new(
        request.Id,
        request.RequestNumber,
        request.Status.ToString(),
        request.Environment.ToString(),
        request.ProjectType.ToString(),
        request.Priority.ToString(),
        request.RequestorUserId,
        request.CreatedAt,
        request.UpdatedAt,
        request.WorkflowStages
            .OrderBy(ws => ws.Id)
            .Select(ws => new WorkflowStageResponse(
                ws.Id, ws.StageName, ws.Status.ToString(), ws.AssignedRole, ws.StartedAt, ws.CompletedAt, ws.Comments))
            .ToList());
}
