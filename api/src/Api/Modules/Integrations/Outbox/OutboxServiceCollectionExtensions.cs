namespace Api.Modules.Integrations.Outbox;

/// <summary>
/// Outbox module wiring (design spec Section 2.3 "Integration Bus"). Kept as
/// its own registration method (rather than folded into
/// IntegrationsServiceCollectionExtensions) so it can be added to Program.cs
/// as a single additive line.
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOutboxModule(this IServiceCollection services)
    {
        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName);

        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
