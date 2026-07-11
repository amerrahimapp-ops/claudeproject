namespace Api.Modules.Ai;

public record AiEvaluationRequest(int RequestId, string RequestSummaryJson, string UtilizationMetricsJson);

public record AiEvaluationResponse(double Score, string Recommendation, string[] Flags);

/// <summary>
/// Result of an evaluation attempt. Response is null if the model's output
/// didn't parse into the expected structured shape - callers must handle
/// this (e.g. fall back to "Skip AI" manual approval per spec Section 11)
/// rather than assume evaluation always succeeds.
/// </summary>
public record AiEvaluationResult(bool Success, AiEvaluationResponse? Response, string Prompt, string RawResponse, string? ErrorMessage);

public interface IAiEvaluationClient
{
    Task<AiEvaluationResult> EvaluateAsync(AiEvaluationRequest request, CancellationToken cancellationToken = default);
}
