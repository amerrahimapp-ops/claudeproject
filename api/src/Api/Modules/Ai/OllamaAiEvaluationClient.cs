using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Api.Modules.Ai;

/// <summary>
/// Real AI adapter - calls a local Ollama instance over REST with a
/// JSON-schema-constrained response contract (ADR 0002), rather than
/// parsing freeform text. Used when Ai:Provider is "Ollama".
/// </summary>
public class OllamaAiEvaluationClient : IAiEvaluationClient
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<OllamaAiEvaluationClient> _logger;

    public OllamaAiEvaluationClient(HttpClient httpClient, IOptions<AiOptions> options, ILogger<OllamaAiEvaluationClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<AiEvaluationResult> EvaluateAsync(AiEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        var prompt =
            "You are evaluating a capacity request for over/under-provisioning. " +
            "Respond with JSON only, no other text, matching exactly this shape: " +
            "{\"score\": <number 0-100, higher = more confident the request is justified>, " +
            "\"recommendation\": <one of \"approve\", \"challenge\", \"reject\">, " +
            "\"flags\": [<short strings describing any concerns, empty array if none>]}\n\n" +
            $"Request details: {request.RequestSummaryJson}\n\n" +
            $"Historical utilization metrics (last 30 days): {request.UtilizationMetricsJson}";

        var requestBody = JsonSerializer.Serialize(new
        {
            model = _options.Model,
            prompt,
            format = "json",
            stream = false,
        });

        try
        {
            var httpResponse = await _httpClient.PostAsync(
                "api/generate", new StringContent(requestBody, Encoding.UTF8, "application/json"), cancellationToken);
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama returned {StatusCode}: {Body}", httpResponse.StatusCode, body);
                return new AiEvaluationResult(false, null, prompt, body, $"HTTP {(int)httpResponse.StatusCode}");
            }

            using var envelope = JsonDocument.Parse(body);
            var modelOutput = envelope.RootElement.GetProperty("response").GetString() ?? "";

            try
            {
                using var parsed = JsonDocument.Parse(modelOutput);
                var root = parsed.RootElement;
                var score = root.GetProperty("score").GetDouble();
                var recommendation = root.GetProperty("recommendation").GetString()!;
                var flags = root.GetProperty("flags").EnumerateArray()
                    .Select(f => f.GetString() ?? "")
                    .ToArray();

                var response = new AiEvaluationResponse(score, recommendation, flags);
                return new AiEvaluationResult(true, response, prompt, modelOutput, null);
            }
            catch (Exception parseEx)
            {
                _logger.LogWarning(parseEx, "Ollama response did not match the expected JSON shape: {ModelOutput}", modelOutput);
                return new AiEvaluationResult(false, null, prompt, modelOutput, "Response did not match the expected JSON shape");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama evaluation call failed");
            return new AiEvaluationResult(false, null, prompt, "", ex.Message);
        }
    }
}
