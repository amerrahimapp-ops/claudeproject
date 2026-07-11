using Api.Modules.Integrations.Email;
using Api.Modules.Integrations.Grafana;

namespace Api.Modules.Integrations;

/// <summary>
/// Integrations module wiring (Grafana, Email, Jira, AI adapter sidecar).
/// Email and Grafana are wired (Phase 4); Jira/AI adapter land alongside.
/// </summary>
public static class IntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        // "Provider" config switch, same pattern as Auth (see
        // AuthServiceCollectionExtensions). "Mock" is the safe default so
        // tests/CI never need real Mailtrap credentials.
        var emailProvider = configuration["Email:Provider"] ?? "Mock";
        switch (emailProvider)
        {
            case "Mailtrap":
                services.AddSingleton<IEmailClient, MailtrapSmtpEmailClient>();
                break;
            case "Mock":
            default:
                services.AddSingleton<IEmailClient, MockEmailClient>();
                break;
        }

        services.Configure<GrafanaOptions>(configuration.GetSection(GrafanaOptions.SectionName));

        var grafanaProvider = configuration["Grafana:Provider"] ?? "Mock";
        switch (grafanaProvider)
        {
            case "Grafana":
                services.AddHttpClient<IGrafanaClient, GrafanaClient>();
                break;
            case "Mock":
            default:
                services.AddSingleton<IGrafanaClient, MockGrafanaClient>();
                break;
        }

        return services;
    }
}
