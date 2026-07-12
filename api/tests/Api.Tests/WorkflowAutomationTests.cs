using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Modules.Ai;
using Api.Modules.Integrations.Grafana;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Tests;

/// <summary>
/// Phase 7b: the automatic ai_evaluation chain (WorkflowAutomationService)
/// and the ai-insights read endpoint (Phase 7c builds its "AI Insights"
/// panel directly against the response shape asserted here).
/// </summary>
[Collection("Integration")]
public class WorkflowAutomationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WorkflowAutomationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiEvaluationClient>();
                services.AddSingleton<IAiEvaluationClient, CapturingAiEvaluationClient>();
                services.RemoveAll<IGrafanaClient>();
                services.AddSingleton<IGrafanaClient, MockGrafanaClient>();
            });
        });
    }

    [Fact]
    public async Task SubmittingRequest_FeedsRealRequestDataIntoAiEvaluation_NotPlaceholderText()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync("/api/v1/requests", new
        {
            title = "Distinctive Automation Test Title",
            department = "IT",
            projectName = "Automation Test Project",
            projectCode = "ATP-042",
            sponsor = "Jane Sponsor",
            environment = "Prod",
            projectType = "New",
            priority = "Medium",
            startDate = "2026-08-01T00:00:00Z",
            endDate = "2026-12-31T00:00:00Z",
            resources = new[] { new { resourceType = "Storage", currentValue = 200, requestedValue = 260 } },
            servers = new[]
            {
                new
                {
                    hostname = "automation-test-host-01",
                    ipAddress = "10.9.9.9",
                    os = "RHEL 8.6",
                    isPhysical = false,
                    resourceType = "Storage",
                    currentValue = 200,
                    requestedValue = 260,
                    mountPoint = "/data",
                    platform = "Unix",
                    drApplicable = true,
                    appTier = "Tier 1",
                },
            },
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = created.GetProperty("id").GetInt32();

        var transitionResponse = await client.PostAsJsonAsync(
            $"/api/v1/requests/{requestId}/transition",
            new { targetStage = "submitted", comments = (string?)null });
        transitionResponse.EnsureSuccessStatusCode();

        var body = await transitionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AiReviewed", body.GetProperty("status").GetString());

        // The capturing test double proves the orchestrator built its
        // summary from the request's real fields (title/hostname), not
        // placeholder text.
        Assert.NotNull(CapturingAiEvaluationClient.LastRequest);
        Assert.Contains("Distinctive Automation Test Title", CapturingAiEvaluationClient.LastRequest!.RequestSummaryJson);
        Assert.Contains("automation-test-host-01", CapturingAiEvaluationClient.LastRequest.RequestSummaryJson);
        Assert.Equal(requestId, CapturingAiEvaluationClient.LastRequest.RequestId);
    }

    [Fact]
    public async Task AiInsights_ReturnsLatestEvaluationAndServerUtilization()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync("/api/v1/requests", new
        {
            title = "Ai Insights Test",
            department = "IT",
            projectName = "Ai Insights Project",
            projectCode = "AIP-001",
            sponsor = "Jane Sponsor",
            environment = "Prod",
            projectType = "New",
            priority = "Medium",
            startDate = "2026-08-01T00:00:00Z",
            endDate = "2026-12-31T00:00:00Z",
            resources = new[] { new { resourceType = "Storage", currentValue = 100, requestedValue = 150 } },
            servers = new[]
            {
                new
                {
                    hostname = "insights-host-01",
                    ipAddress = "10.1.1.1",
                    os = (string?)null,
                    isPhysical = true,
                    resourceType = "Storage",
                    currentValue = 100,
                    requestedValue = 150,
                    mountPoint = (string?)null,
                    platform = "Unix",
                    drApplicable = false,
                    appTier = (string?)null,
                },
            },
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = created.GetProperty("id").GetInt32();

        var transitionResponse = await client.PostAsJsonAsync(
            $"/api/v1/requests/{requestId}/transition",
            new { targetStage = "submitted", comments = (string?)null });
        transitionResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/v1/requests/{requestId}/ai-insights");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var latestEvaluation = body.GetProperty("latestEvaluation");
        Assert.True(latestEvaluation.TryGetProperty("score", out _));
        Assert.True(latestEvaluation.TryGetProperty("recommendation", out _));
        Assert.True(latestEvaluation.TryGetProperty("flags", out _));
        Assert.True(latestEvaluation.TryGetProperty("evaluatedAt", out _));

        var serverUtilization = body.GetProperty("serverUtilization").EnumerateArray().ToList();
        Assert.Single(serverUtilization);
        var server = serverUtilization[0];
        Assert.Equal("insights-host-01", server.GetProperty("hostname").GetString());
        Assert.True(server.GetProperty("success").GetBoolean());
        Assert.True(server.GetProperty("cpu").TryGetProperty("avg", out _));
        Assert.True(server.GetProperty("memory").TryGetProperty("avg", out _));
        Assert.True(server.GetProperty("disk").TryGetProperty("avg", out _));
    }

    [Fact]
    public async Task AiInsights_ForNonExistentRequest_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/requests/999999/ai-insights");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}

/// <summary>
/// Test double that records the last request it was called with (to verify
/// the orchestrator feeds real request data through) and always succeeds
/// like MockAiEvaluationClient.
/// </summary>
public class CapturingAiEvaluationClient : IAiEvaluationClient
{
    public static AiEvaluationRequest? LastRequest { get; private set; }

    public Task<AiEvaluationResult> EvaluateAsync(AiEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        var response = new AiEvaluationResponse(80, "approve", []);
        return Task.FromResult(new AiEvaluationResult(
            true, response, "[captured prompt]", "{\"score\":80,\"recommendation\":\"approve\",\"flags\":[]}", null));
    }
}
