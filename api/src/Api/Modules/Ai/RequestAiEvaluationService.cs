using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Api.Modules.Integrations.Grafana;
using Api.Modules.Requests;

namespace Api.Modules.Ai;

public interface IRequestAiEvaluationService
{
    /// <summary>
    /// Builds the real request summary (title/department/project/resources/
    /// servers/justifications - not placeholder text) plus real Grafana
    /// utilization data for the request's servers (spec 4.5/10.4), calls
    /// <see cref="IAiEvaluationClient"/>, and persists the outcome to
    /// <c>AiEvaluations</c> regardless of success/failure (ADR 0002's full
    /// audit trail). <paramref name="request"/> must already have
    /// <c>RequestServers</c> and <c>Justifications</c> eager-loaded.
    /// </summary>
    Task<AiEvaluationResult> EvaluateAndPersistAsync(Request request, CancellationToken cancellationToken = default);
}

public class RequestAiEvaluationService : IRequestAiEvaluationService
{
    private readonly IAiEvaluationClient _aiClient;
    private readonly IGrafanaUtilizationService _utilizationService;
    private readonly CapacityDbContext _db;
    private readonly ILogger<RequestAiEvaluationService> _logger;

    public RequestAiEvaluationService(
        IAiEvaluationClient aiClient,
        IGrafanaUtilizationService utilizationService,
        CapacityDbContext db,
        ILogger<RequestAiEvaluationService> logger)
    {
        _aiClient = aiClient;
        _utilizationService = utilizationService;
        _db = db;
        _logger = logger;
    }

    public async Task<AiEvaluationResult> EvaluateAndPersistAsync(Request request, CancellationToken cancellationToken = default)
    {
        var summaryJson = JsonSerializer.Serialize(new
        {
            request.RequestNumber,
            request.Title,
            request.Department,
            request.ProjectName,
            request.ProjectCode,
            request.Sponsor,
            Environment = request.Environment.ToString(),
            ProjectType = request.ProjectType.ToString(),
            Priority = request.Priority.ToString(),
            request.StartDate,
            request.EndDate,
            request.Description,
            CurrentCapacity = RequestMapper.DeserializeCapacityMap(request.CurrentCapacity),
            RequestedCapacity = RequestMapper.DeserializeCapacityMap(request.RequestedCapacity),
            UpliftPercentages = RequestMapper.DeserializeUpliftMap(request.UpliftPercentages),
            Servers = request.RequestServers.Select(s => new
            {
                s.Hostname,
                s.IpAddress,
                s.Os,
                s.IsPhysical,
                ResourceType = s.ResourceType.ToString(),
                s.CurrentValue,
                s.RequestedValue,
                s.MountPoint,
                Platform = s.Platform.ToString(),
                s.DrApplicable,
                s.AppTier,
            }),
            Justifications = request.Justifications.Select(j => new
            {
                ResourceType = j.ResourceType.ToString(),
                j.QuestionKey,
                j.AnswerText,
            }),
        });

        var hostnames = request.RequestServers
            .Select(s => s.Hostname)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var utilization = hostnames.Count > 0
            ? await _utilizationService.GetUtilizationAsync(hostnames, cancellationToken)
            : [];
        var utilizationJson = JsonSerializer.Serialize(utilization);

        var result = await _aiClient.EvaluateAsync(
            new AiEvaluationRequest(request.Id, summaryJson, utilizationJson), cancellationToken);

        _db.AiEvaluations.Add(new AiEvaluation
        {
            RequestId = request.Id,
            Prompt = result.Prompt,
            RawResponse = result.RawResponse,
            Score = result.Response?.Score,
            Recommendation = result.Response?.Recommendation,
            FlagsJson = result.Response is null ? null : JsonSerializer.Serialize(result.Response.Flags),
            EvaluatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(cancellationToken);

        if (!result.Success)
        {
            _logger.LogError(
                "AI evaluation failed for request {RequestId}: {Error}", request.Id, result.ErrorMessage);
        }

        return result;
    }
}
