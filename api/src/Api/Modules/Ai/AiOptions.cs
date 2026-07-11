namespace Api.Modules.Ai;

/// <summary>
/// Binds the "Ai:Ollama" config section. Local Ollama is the dev/Phase-1
/// implementation (ADR 0002) - BaseUrl/Model point at a local instance by
/// default, no credentials needed since it's not a hosted service.
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai:Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5-coder:7b";
}
