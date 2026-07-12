namespace Api.Modules.Integrations.Grafana;

/// <summary>Avg/max/p95 across the trailing-30-day window, per ADR 0003. Null fields mean no data points were returned.</summary>
public record MetricStats(double? Avg, double? Max, double? P95);

/// <summary>
/// One server's utilization snapshot. <see cref="Success"/> is false if any
/// of the three underlying Grafana queries failed (network error, non-2xx,
/// unparseable body) - callers should treat that server's stats as
/// unavailable rather than assume zeros.
/// </summary>
public record ServerUtilization(string Hostname, bool Success, string? ErrorMessage, MetricStats? Cpu, MetricStats? Memory, MetricStats? Disk);

public interface IGrafanaUtilizationService
{
    /// <summary>
    /// Fetches CPU/memory/disk utilization % for each hostname, trailing 30
    /// days (ADR 0003's pinned metric set), used both by the automatic AI
    /// evaluation (spec 4.5/10.4 - "Input: Request data + Grafana
    /// utilization metrics") and by the ai-insights read endpoint.
    /// </summary>
    Task<IReadOnlyList<ServerUtilization>> GetUtilizationAsync(IEnumerable<string> hostnames, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thin aggregation layer over <see cref="IGrafanaClient"/>'s raw
/// query_range wrapper. The exact PromQL metric names below
/// (cpu_utilization_percent / memory_utilization_percent /
/// disk_utilization_percent) are placeholders consistent with ADR 0003's
/// pinned metric set - the real names depend on whatever's actually scraped
/// in a given deployment and may need revision once that's finalized (same
/// caveat as GrafanaClient's own query construction). MockGrafanaClient
/// ignores the query text entirely, so this works against the dev/test mock
/// regardless.
/// </summary>
public class GrafanaUtilizationService : IGrafanaUtilizationService
{
    private readonly IGrafanaClient _grafanaClient;
    private readonly ILogger<GrafanaUtilizationService> _logger;

    public GrafanaUtilizationService(IGrafanaClient grafanaClient, ILogger<GrafanaUtilizationService> logger)
    {
        _grafanaClient = grafanaClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServerUtilization>> GetUtilizationAsync(
        IEnumerable<string> hostnames, CancellationToken cancellationToken = default)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-30);
        var results = new List<ServerUtilization>();

        foreach (var hostname in hostnames.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var cpu = await QueryMetricAsync($"cpu_utilization_percent{{instance=\"{hostname}\"}}", from, to, cancellationToken);
            var memory = await QueryMetricAsync($"memory_utilization_percent{{instance=\"{hostname}\"}}", from, to, cancellationToken);
            var disk = await QueryMetricAsync($"disk_utilization_percent{{instance=\"{hostname}\"}}", from, to, cancellationToken);

            var success = cpu.Success && memory.Success && disk.Success;
            var error = success
                ? null
                : string.Join("; ", new[] { cpu.Error, memory.Error, disk.Error }.Where(e => e is not null));

            results.Add(new ServerUtilization(hostname, success, error, cpu.Stats, memory.Stats, disk.Stats));
        }

        return results;
    }

    private async Task<(bool Success, string? Error, MetricStats? Stats)> QueryMetricAsync(
        string promQlQuery, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var result = await _grafanaClient.QueryRangeAsync(promQlQuery, from, to, "1h", cancellationToken);
        if (!result.Success || result.RawResponse is null)
        {
            _logger.LogWarning("Grafana utilization query failed for {Query}: {Error}", promQlQuery, result.ErrorMessage);
            return (false, result.ErrorMessage, null);
        }

        try
        {
            var values = new List<double>();
            var resultArray = result.RawResponse.RootElement.GetProperty("data").GetProperty("result");
            foreach (var series in resultArray.EnumerateArray())
            {
                foreach (var point in series.GetProperty("values").EnumerateArray())
                {
                    if (double.TryParse(point[1].GetString(), out var value))
                    {
                        values.Add(value);
                    }
                }
            }

            if (values.Count == 0)
            {
                return (true, null, new MetricStats(null, null, null));
            }

            values.Sort();
            var avg = Math.Round(values.Average(), 2);
            var max = Math.Round(values[^1], 2);
            var p95Index = Math.Clamp((int)Math.Ceiling(0.95 * values.Count) - 1, 0, values.Count - 1);
            var p95 = Math.Round(values[p95Index], 2);

            return (true, null, new MetricStats(avg, max, p95));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Grafana response for {Query}", promQlQuery);
            return (false, "Failed to parse Grafana response", null);
        }
    }
}
