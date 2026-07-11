using System.Text.Json;

namespace Api.Modules.Integrations.Grafana;

/// <summary>
/// Raw query_range result. RawResponse is the Prometheus-format JSON body
/// as-is - the AI adapter (built after this) parses specific series out of
/// it once the exact PromQL queries for CPU/RAM/disk utilization (ADR 0003)
/// are finalized against whatever's actually scraped in a given deployment.
/// </summary>
public record GrafanaQueryResult(bool Success, JsonDocument? RawResponse, string? ErrorMessage);

public interface IGrafanaClient
{
    /// <summary>
    /// Low-level query_range wrapper matching spec Section 8:
    /// GET /api/datasources/proxy/{id}/api/v1/query_range.
    /// </summary>
    Task<GrafanaQueryResult> QueryRangeAsync(
        string promQlQuery, DateTimeOffset from, DateTimeOffset to, string step, CancellationToken cancellationToken = default);
}
