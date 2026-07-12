namespace Api.Modules.Ai;

/// <summary>
/// AI evaluation module wiring (Ollama adapter per ADR 0002).
/// </summary>
public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        // "Provider" config switch, same pattern as Auth/Email/Grafana.
        // "Mock" is the safe default so tests/CI never need a real Ollama
        // instance running.
        var aiProvider = configuration["Ai:Provider"] ?? "Mock";
        switch (aiProvider)
        {
            case "Ollama":
                services.AddHttpClient<IAiEvaluationClient, OllamaAiEvaluationClient>();
                break;
            case "Mock":
            default:
                services.AddSingleton<IAiEvaluationClient, MockAiEvaluationClient>();
                break;
        }

        services.AddScoped<IRequestAiEvaluationService, RequestAiEvaluationService>();

        return services;
    }
}
