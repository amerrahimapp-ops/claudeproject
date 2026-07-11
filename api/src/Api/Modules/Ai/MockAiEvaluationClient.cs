namespace Api.Modules.Ai;

/// <summary>
/// Dev/test mock - returns a canned evaluation instead of calling a real
/// Ollama instance. Used when Ai:Provider is "Mock" or unset, so tests/CI
/// never need a local Ollama running.
/// </summary>
public class MockAiEvaluationClient : IAiEvaluationClient
{
    public Task<AiEvaluationResult> EvaluateAsync(AiEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        var response = new AiEvaluationResponse(80, "approve", []);
        return Task.FromResult(new AiEvaluationResult(
            true, response, "[mock prompt]", "{\"score\":80,\"recommendation\":\"approve\",\"flags\":[]}", null));
    }
}
