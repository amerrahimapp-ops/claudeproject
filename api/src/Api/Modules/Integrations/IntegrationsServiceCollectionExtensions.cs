namespace Api.Modules.Integrations;

/// <summary>
/// Integrations module wiring (Grafana, Email, Jira, AI adapter sidecar).
/// Empty scaffolding for the Foundation phase — real clients land in a
/// later phase.
/// </summary>
public static class IntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services)
    {
        return services;
    }
}
