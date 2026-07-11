using Api.Modules.Integrations.Email;

namespace Api.Modules.Integrations;

/// <summary>
/// Integrations module wiring (Grafana, Email, Jira, AI adapter sidecar).
/// Email is wired (Phase 4); Grafana/Jira/AI adapter land alongside it.
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

        return services;
    }
}
