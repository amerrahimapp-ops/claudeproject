using System.Text.Json;

namespace Api.Modules.Integrations.Grafana;

/// <summary>
/// Dev/test mock - returns a canned Prometheus-shaped result instead of
/// calling a real Grafana instance. Used when Grafana:Provider is "Mock"
/// or unset, so tests/CI never need real Grafana credentials.
/// </summary>
public class MockGrafanaClient : IGrafanaClient
{
    private static readonly string CannedResponse = """
        {
          "status": "success",
          "data": {
            "resultType": "matrix",
            "result": [
              {
                "metric": { "__name__": "mock_metric" },
                "values": [[1700000000, "42.5"], [1700003600, "45.1"]]
              }
            ]
          }
        }
        """;

    public Task<GrafanaQueryResult> QueryRangeAsync(
        string promQlQuery, DateTimeOffset from, DateTimeOffset to, string step, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GrafanaQueryResult(true, JsonDocument.Parse(CannedResponse), null));
    }
}
