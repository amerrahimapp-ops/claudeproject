using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Data;
using Api.Modules.Ai;
using Api.Modules.Integrations.Grafana;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Tests;

/// <summary>
/// Phase 8c queue position indicator (spec 6.3): "You are #3 waiting for
/// Capacity Review". <c>GET /api/v1/requests/{id}</c> computes a 1-based
/// <c>queuePosition</c> among other requests currently sitting in the same
/// CapacityReview/InfraApproval stage, oldest-waiting-first — null for every
/// other stage (Draft/Submitted/AiEvaluation/AiReviewed are either
/// not-yet-submitted or system-automatic). Same WebApplicationFactory + real
/// local MySQL integration-test pattern as WorkflowEngineTests, forcing Mock
/// AI/Grafana so the automatic submitted -&gt; ai_evaluation -&gt; ai_reviewed
/// cascade doesn't hit real Ollama/Grafana.
///
/// Prerequisite: `docker compose up -d mysql` from the repo root.
/// </summary>
[Collection("Integration")]
public class QueuePositionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public QueuePositionEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiEvaluationClient>();
                services.AddSingleton<IAiEvaluationClient, MockAiEvaluationClient>();
                services.RemoveAll<IGrafanaClient>();
                services.AddSingleton<IGrafanaClient, MockGrafanaClient>();
            });
        });
    }

    [Fact]
    public async Task ThreeRequests_AdvancedToCapacityReviewInOrder_GetSequentialPositions()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // This is a shared dev database (other test classes / earlier manual
        // verification runs may leave requests sitting in CapacityReview), so
        // asserting an absolute #1/#2/#3 would be flaky. Instead, capture
        // this test's own three requests' positions and assert they land in
        // strict, consecutive order relative to whatever baseline already
        // exists — that's what actually matters for spec 6.3's "#3 waiting"
        // transparency guarantee (correct relative ordering), not a clean
        // slate.
        var firstId = await CreateDraftRequestAsync(client);
        await AdvanceToCapacityReviewAsync(client, firstId);
        // WorkflowEngine.TransitionAsync uses DateTime.UtcNow for UpdatedAt,
        // which on a fast local test run can tie down to the tick for
        // requests transitioned back-to-back. A tiny delay between each
        // guarantees a strict, observable ordering rather than relying on
        // sub-millisecond scheduler timing.
        await Task.Delay(50);

        var secondId = await CreateDraftRequestAsync(client);
        await AdvanceToCapacityReviewAsync(client, secondId);
        await Task.Delay(50);

        var thirdId = await CreateDraftRequestAsync(client);
        await AdvanceToCapacityReviewAsync(client, thirdId);

        var firstPosition = await GetQueuePositionAsync(client, firstId);
        var secondPosition = await GetQueuePositionAsync(client, secondId);
        var thirdPosition = await GetQueuePositionAsync(client, thirdId);

        Assert.NotNull(firstPosition);
        Assert.Equal(firstPosition + 1, secondPosition);
        Assert.Equal(firstPosition + 2, thirdPosition);
    }

    [Fact]
    public async Task RequestNotInHumanReviewStage_HasNullQueuePosition()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestId = await CreateDraftRequestAsync(client);

        var response = await client.GetAsync($"/api/v1/requests/{requestId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("queuePosition").ValueKind);
    }

    [Fact]
    public async Task RequestResolvedOutOfQueue_DoesNotCountTowardOthersStillWaiting()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var capacityManagerToken = await GetAccessTokenAsync(client, "capacitymanager.dev");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requestorToken);
        var firstId = await CreateDraftRequestAsync(client);
        await AdvanceToCapacityReviewAsync(client, firstId);
        await Task.Delay(50);

        var secondId = await CreateDraftRequestAsync(client);
        await AdvanceToCapacityReviewAsync(client, secondId);

        var secondPositionBeforeFirstLeaves = await GetQueuePositionAsync(client, secondId);

        // Move the first request onward to infra_approval (CapacityManager
        // role) — it leaves the CapacityReview queue entirely, so the second
        // request's position should drop by exactly one (it no longer counts
        // toward "others still waiting"), regardless of whatever baseline
        // pre-existing CapacityReview requests are in this shared dev DB.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", capacityManagerToken);
        var advanceResponse = await client.PostAsJsonAsync(
            $"/api/v1/requests/{firstId}/transition",
            new { targetStage = "infra_approval", comments = (string?)null });
        advanceResponse.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requestorToken);
        var secondPositionAfterFirstLeaves = await GetQueuePositionAsync(client, secondId);

        Assert.Equal(secondPositionBeforeFirstLeaves - 1, secondPositionAfterFirstLeaves);
    }

    private static async Task<int?> GetQueuePositionAsync(HttpClient client, int requestId)
    {
        var response = await client.GetAsync($"/api/v1/requests/{requestId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var prop = body.GetProperty("queuePosition");
        return prop.ValueKind == JsonValueKind.Null ? null : prop.GetInt32();
    }

    private static async Task AdvanceToCapacityReviewAsync(HttpClient client, int requestId)
    {
        // submitted auto-cascades to ai_reviewed (WorkflowAutomationService).
        var submitResponse = await client.PostAsJsonAsync(
            $"/api/v1/requests/{requestId}/transition",
            new { targetStage = "submitted", comments = (string?)null });
        submitResponse.EnsureSuccessStatusCode();

        var capacityReviewResponse = await client.PostAsJsonAsync(
            $"/api/v1/requests/{requestId}/transition",
            new { targetStage = "capacity_review", comments = (string?)null });
        capacityReviewResponse.EnsureSuccessStatusCode();
    }

    private static async Task<int> CreateDraftRequestAsync(HttpClient client)
    {
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
