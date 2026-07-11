namespace Api.Data.Entities;

/// <summary>
/// The single source of truth for the mapping between <see cref="RequestStatus"/>
/// (PascalCase enum members) and the snake_case stage-name strings used by
/// <see cref="WorkflowConfig.StageName"/> / <see cref="WorkflowStage.StageName"/>.
/// All code that needs to convert between the two MUST go through these
/// extension methods rather than hand-rolling string conversions.
/// </summary>
public static class StageNameExtensions
{
    private static readonly IReadOnlyDictionary<RequestStatus, string> StatusToStageName =
        new Dictionary<RequestStatus, string>
        {
            [RequestStatus.Draft] = "draft",
            [RequestStatus.Submitted] = "submitted",
            [RequestStatus.AiEvaluation] = "ai_evaluation",
            [RequestStatus.AiReviewed] = "ai_reviewed",
            [RequestStatus.CapacityReview] = "capacity_review",
            [RequestStatus.InfraApproval] = "infra_approval",
            [RequestStatus.Done] = "done",
            [RequestStatus.Rejected] = "rejected",
            [RequestStatus.Deferred] = "deferred",
        };

    private static readonly IReadOnlyDictionary<string, RequestStatus> StageNameToStatus =
        StatusToStageName.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>Maps a <see cref="RequestStatus"/> to its snake_case stage-name string.</summary>
    public static string ToStageName(this RequestStatus status) => StatusToStageName[status];

    /// <summary>
    /// Maps a snake_case stage-name string to its <see cref="RequestStatus"/>.
    /// Throws if the stage name is unrecognized.
    /// </summary>
    public static RequestStatus ToRequestStatus(this string stageName) =>
        StageNameToStatus.TryGetValue(stageName, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(stageName), stageName, "Unknown workflow stage name.");

    /// <summary>Non-throwing variant of <see cref="ToRequestStatus"/> for validating untrusted input.</summary>
    public static bool TryToRequestStatus(this string stageName, out RequestStatus status) =>
        StageNameToStatus.TryGetValue(stageName, out status);
}
