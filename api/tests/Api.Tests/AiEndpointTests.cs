using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Data;
using Api.Modules.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Tests;

/// <summary>
/// Phase 4 test-ai-evaluation endpoint: confirms the Admin-only auth guard
/// works, that a request succeeds with the default Mock provider (no real
/// Ollama needed), and that every attempt is logged to ai_evaluations
/// regardless of outcome (ADR 0002 - full audit trail).
/// </summary>
[Collection("Integration")]
public class AiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AiEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            // Force Mock regardless of local appsettings.Development.json
            // (e.g. real Ollama configured). A config-key override
            // (ConfigureAppConfiguration) is NOT reliable here - the
            // Provider switch is read once in Program.cs's AddAiModule
            // call during host build, and config added via
            // ConfigureAppConfiguration doesn't consistently win that race
            // (confirmed: this test flakily called real Ollama and got
            // "challenge" instead of the mocked "approve"). Replacing the
            // DI registration directly is deterministic.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiEvaluationClient>();
                services.AddSingleton<IAiEvaluationClient, MockAiEvaluationClient>();
            });
        });
    }

    [Fact]
    public async Task TestAiEvaluation_AsAdmin_WithMockProvider_SucceedsAndLogsEvaluation()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var requestId = await CreateDraftRequestAsync(client, requestorToken);

        var adminToken = await GetAccessTokenAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/test-ai-evaluation", new { requestId, utilizationMetricsJson = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
        var logged = await db.AiEvaluations.FirstOrDefaultAsync(ae => ae.RequestId == requestId);
        Assert.NotNull(logged);
        Assert.NotNull(logged!.Score);
        Assert.Equal("approve", logged.Recommendation);
    }

    [Fact]
    public async Task TestAiEvaluation_AsNonAdmin_IsForbidden()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var requestId = await CreateDraftRequestAsync(client, requestorToken);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requestorToken);
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/test-ai-evaluation", new { requestId, utilizationMetricsJson = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TestAiEvaluation_ForNonExistentRequest_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var adminToken = await GetAccessTokenAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/test-ai-evaluation", new { requestId = 999_999, utilizationMetricsJson = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<int> CreateDraftRequestAsync(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/v1/requests", TestRequestPayloads.ValidCreateRequest());
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
