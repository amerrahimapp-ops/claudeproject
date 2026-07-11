namespace Api.Modules.Integrations.Grafana;

/// <summary>
/// Binds the "Grafana" config section. DatasourceId defaults to the
/// account's default Prometheus datasource - override if a deployment's
/// default differs (check GET {BaseUrl}/api/datasources for the right id).
/// </summary>
public class GrafanaOptions
{
    public const string SectionName = "Grafana";

    public string BaseUrl { get; set; } = null!;
    public string ApiToken { get; set; } = null!;
    public int DatasourceId { get; set; } = 1;
}
