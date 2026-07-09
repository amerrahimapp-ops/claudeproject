namespace Api.Modules.Ai;

/// <summary>
/// AI evaluation module wiring (Ollama adapter per ADR 0002). Empty
/// scaffolding for the Foundation phase — IAiEvaluationClient and its
/// implementations land in a later phase (and per CLAUDE.md, AI adapter
/// design stays on Claude Code rather than being delegated).
/// </summary>
public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiModule(this IServiceCollection services)
    {
        return services;
    }
}
