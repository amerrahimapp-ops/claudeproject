using System.Text.Json;
using Api.Data.Entities;

namespace Api.Modules.Requests;

// ---------------------------------------------------------------------
// Create-request request body (Phase 7a). Single-POST-at-the-end
// contract: the whole 5-step wizard (spec 8.4 — Requestor Info / Project
// Info / Resources / Server Details / Justifications) is submitted as one
// payload rather than a multi-step draft-then-PATCH flow. "Requestor Info"
// (Name/PF/Email/Contact/Department) is deliberately NOT part of this body
// except for Department — the rest is read-only display data the frontend
// already has from the JWT/User (DisplayName, Email, PfNumber), so there is
// nothing for the server to accept or validate there. See
// docs/progress/phase-7a-status.md for the full rationale.
// ---------------------------------------------------------------------

/// <summary>One resource-type selection from wizard step 3 (Resources).</summary>
public record CreateRequestResourceRequest(string ResourceType, decimal CurrentValue, decimal RequestedValue);

/// <summary>One server row from wizard step 4 (Server Details).</summary>
public record CreateRequestServerRequest(
    string Hostname,
    string IpAddress,
    string? Os,
    bool IsPhysical,
    string ResourceType,
    decimal CurrentValue,
    decimal RequestedValue,
    string? MountPoint,
    string Platform,
    bool DrApplicable,
    string? AppTier);

/// <summary>One Q&amp;A pair from wizard step 5 (Justifications).</summary>
public record CreateRequestJustificationRequest(string ResourceType, string QuestionKey, string AnswerText);

public record CreateRequestRequest(
    string Title,
    string Department,
    string ProjectName,
    string ProjectCode,
    string Sponsor,
    string Environment,
    string ProjectType,
    string Priority,
    DateTime StartDate,
    DateTime EndDate,
    string? Description,
    IReadOnlyList<CreateRequestResourceRequest> Resources,
    IReadOnlyList<CreateRequestServerRequest>? Servers,
    IReadOnlyList<CreateRequestJustificationRequest>? Justifications);

// ---------------------------------------------------------------------
// Response shapes
// ---------------------------------------------------------------------

public record ResourceSummaryResponse(
    string ResourceType,
    decimal CurrentValue,
    decimal RequestedValue,
    /// <summary>
    /// Server-computed uplift % — (requested - current) / current * 100,
    /// rounded to 2 dp. Null when current is 0 (division undefined) rather
    /// than an arbitrary sentinel value.
    /// </summary>
    decimal? UpliftPercent);

public record RequestServerResponse(
    int Id,
    string Hostname,
    string IpAddress,
    string? Os,
    bool IsPhysical,
    string ResourceType,
    decimal CurrentValue,
    decimal RequestedValue,
    string? MountPoint,
    string Platform,
    bool DrApplicable,
    string? AppTier);

public record JustificationResponse(
    int Id,
    string ResourceType,
    string QuestionKey,
    string AnswerText,
    string? AttachmentPaths);

public record WorkflowStageResponse(
    int Id,
    string StageName,
    string Status,
    string? AssignedRole,
    int? AssignedUserId,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Comments);

public record RequestResponse(
    int Id,
    string RequestNumber,
    string Status,
    string Title,
    string Department,
    string ProjectName,
    string ProjectCode,
    string Sponsor,
    string Environment,
    string ProjectType,
    string Priority,
    DateTime StartDate,
    DateTime EndDate,
    string? Description,
    IReadOnlyList<ResourceSummaryResponse> Resources,
    int RequestorUserId,
    /// <summary>AD username of the requestor — lets the frontend decide "is this my request" (owner-only actions) without decoding the JWT.</summary>
    string RequestorUsername,
    string RequestorDisplayName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<RequestServerResponse> Servers,
    IReadOnlyList<JustificationResponse> Justifications,
    IReadOnlyList<WorkflowStageResponse> WorkflowStages,
    /// <summary>
    /// 1-based position among other requests currently sitting in the same
    /// human-reviewed stage (spec 6.3), oldest-waiting-first. Null whenever
    /// not applicable (any stage other than CapacityReview/InfraApproval) —
    /// only <c>GET /api/v1/requests/{id}</c> computes this (see
    /// RequestsEndpoints.cs); every other caller of RequestMapper.ToResponse
    /// leaves it null.
    /// </summary>
    int? QueuePosition = null);

/// <summary>User-uploaded (or system-generated, e.g. Phase 7b's Excel report) file attached to a request.</summary>
public record AttachmentResponse(
    int Id,
    string FileName,
    string ContentType,
    int UploadedByUserId,
    string UploadedByDisplayName,
    DateTime UploadedAt);

/// <summary>Single place that maps the Request entity graph to its API shape (also used by the Workflow module).</summary>
public static class RequestMapper
{
    public static RequestResponse ToResponse(Request request, int? queuePosition = null)
    {
        var current = DeserializeCapacityMap(request.CurrentCapacity);
        var requested = DeserializeCapacityMap(request.RequestedCapacity);
        var uplift = DeserializeUpliftMap(request.UpliftPercentages);

        var resources = current.Keys
            .Union(requested.Keys)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(resourceType => new ResourceSummaryResponse(
                resourceType,
                current.GetValueOrDefault(resourceType),
                requested.GetValueOrDefault(resourceType),
                uplift.GetValueOrDefault(resourceType)))
            .ToList();

        return new RequestResponse(
            request.Id,
            request.RequestNumber,
            request.Status.ToString(),
            request.Title,
            request.Department,
            request.ProjectName,
            request.ProjectCode,
            request.Sponsor,
            request.Environment.ToString(),
            request.ProjectType.ToString(),
            request.Priority.ToString(),
            request.StartDate,
            request.EndDate,
            request.Description,
            resources,
            request.RequestorUserId,
            request.RequestorUser.AdUsername,
            request.RequestorUser.DisplayName,
            request.CreatedAt,
            request.UpdatedAt,
            request.RequestServers
                .OrderBy(rs => rs.Id)
                .Select(rs => new RequestServerResponse(
                    rs.Id,
                    rs.Hostname,
                    rs.IpAddress,
                    rs.Os,
                    rs.IsPhysical,
                    rs.ResourceType.ToString(),
                    rs.CurrentValue,
                    rs.RequestedValue,
                    rs.MountPoint,
                    rs.Platform.ToString(),
                    rs.DrApplicable,
                    rs.AppTier))
                .ToList(),
            request.Justifications
                .OrderBy(j => j.Id)
                .Select(j => new JustificationResponse(
                    j.Id, j.ResourceType.ToString(), j.QuestionKey, j.AnswerText, j.AttachmentPaths))
                .ToList(),
            request.WorkflowStages
                .OrderBy(ws => ws.Id)
                .Select(ws => new WorkflowStageResponse(
                    ws.Id, ws.StageName, ws.Status.ToString(), ws.AssignedRole, ws.AssignedUserId,
                    ws.StartedAt, ws.CompletedAt, ws.Comments))
                .ToList(),
            queuePosition);
    }

    public static AttachmentResponse ToAttachmentResponse(Attachment attachment) =>
        new(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.UploadedByUserId,
            attachment.UploadedByUser.DisplayName,
            attachment.UploadedAt);

    /// <summary>Internal (not private) so Api.Modules.Ai's evaluation orchestrator can reuse the same parsing rather than duplicating it.</summary>
    internal static Dictionary<string, decimal> DeserializeCapacityMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, decimal>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json)
            ?? new Dictionary<string, decimal>();
    }

    internal static Dictionary<string, decimal?> DeserializeUpliftMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, decimal?>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, decimal?>>(json)
            ?? new Dictionary<string, decimal?>();
    }
}
