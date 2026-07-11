using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Options;

namespace Api.Modules.Integrations.Grafana;

/// <summary>
/// Real Grafana client - queries the configured Prometheus datasource via
/// the proxy API (spec Section 8). Used when Grafana:Provider is "Grafana"
/// (config-driven Provider switch, same pattern as Auth/Email).
/// </summary>
public class GrafanaClient : IGrafanaClient
{
    private readonly HttpClient _httpClient;
    private readonly GrafanaOptions _options;
    private readonly ILogger<GrafanaClient> _logger;

    public GrafanaClient(HttpClient httpClient, IOptions<GrafanaOptions> options, ILogger<GrafanaClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiToken);
    }

    public async Task<GrafanaQueryResult> QueryRangeAsync(
        string promQlQuery, DateTimeOffset from, DateTimeOffset to, string step, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.UrlEncode(promQlQuery);
        var start = from.ToUnixTimeSeconds();
        var end = to.ToUnixTimeSeconds();
        var path = $"api/datasources/proxy/{_options.DatasourceId}/api/v1/query_range" +
                   $"?query={query}&start={start}&end={end}&step={step}";

        try
        {
            var response = await _httpClient.GetAsync(path, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Grafana query_range returned {StatusCode}: {Body}", response.StatusCode, body);
                return new GrafanaQueryResult(false, null, $"HTTP {(int)response.StatusCode}: {body}");
            }

            return new GrafanaQueryResult(true, JsonDocument.Parse(body), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Grafana query_range failed for query {Query}", promQlQuery);
            return new GrafanaQueryResult(false, null, ex.Message);
        }
    }
}
