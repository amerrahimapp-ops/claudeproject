using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Api.Modules.Ai;
using Api.Modules.Integrations.Grafana;
using Api.Modules.Workflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Tests;

/// <summary>
/// Phase 3 workflow engine tests, extended in Phase 7b for the automatic
/// submitted -&gt; ai_evaluation -&gt; ai_reviewed chain (WorkflowAutomationService).
/// Same WebApplicationFactory + real local MySQL integration-test pattern as
/// HealthAndRequestsEndpointTests. Drives the Phase-1 flow (draft ->
/// submitted -> ai_evaluation -> ai_reviewed -> capacity_review ->
/// infra_approval -> done) purely through the public HTTP API, using real
/// JWTs obtained from /api/v1/auth/login for the four MockIdentityProvider
/// dev users (see MockIdentityProvider.cs).
///
/// Forces Mock AI/Grafana providers regardless of local
/// appsettings.Development.json (same reasoning as AiEndpointTests/
/// GrafanaEndpointTests): every "submitted" transition now automatically
/// triggers the AI evaluation chain, so without this override these tests
/// would flakily hit real Ollama/Grafana Cloud.
///
/// Prerequisite: `docker compose up -d mysql` from the repo root.
/// </summary>
[Collection("Integration")]
public class WorkflowEngineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WorkflowEngineTests(WebApplicationFactory<Program> factory)
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
    public async Task Transition_DraftToSubmitted_AutomaticallyReachesAiReviewedWithMockEvaluation()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var requestId = await CreateDraftRequestAsync(client, requestorToken);

        var response = await TransitionAsync(client, requestorToken, requestId, "submitted");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The response reflects the *final* state after the automatic
        // submitted -> ai_evaluation -> ai_reviewed chain, not just the
        // directly-requested "submitted" transition (WorkflowAutomationService).
        Assert.Equal("AiReviewed", body.GetProperty("status").GetString());

        var stages = body.GetProperty("workflowStages").EnumerateArray().ToList();
        var stageNames = stages.Select(s => s.GetProperty("stageName").GetString()).ToList();
        // "draft" is created implicitly on the first-ever transition (no
        // WorkflowStage row exists for it at request-creation time).
        Assert.Equal(new[] { "draft", "submitted", "ai_evaluation", "ai_reviewed" }, stageNames);
        Assert.Equal("Approved", stages[0].GetProperty("status").GetString());
        Assert.Equal("Approved", stages[1].GetProperty("status").GetString());
        Assert.Equal("Approved", stages[2].GetProperty("status").GetString());
        Assert.Equal("InProgress", stages[3].GetProperty("status").GetString());

        await AssertAuditLogWrittenAsync(requestId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
        var evaluation = await db.AiEvaluations.SingleAsync(ae => ae.RequestId == requestId);
        Assert.Equal(80, evaluation.Score);
        Assert.Equal("approve", evaluation.Recommendation);
    }

    [Fact]
    public async Task Transition_SkippingStages_IsRejectedWithNoStateChange()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var requestId = await CreateDraftRequestAsync(client, requestorToken);

        // draft -> capacity_review skips submitted/ai_evaluation/ai_reviewed.
        var response = await TransitionAsync(client, requestorToken, requestId, "capacity_review");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var getResponse = await GetRequestAsync(client, requestorToken, requestId);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("workflowStages").EnumerateArray());
    }

    [Fact]
    public async Task Transition_WithWrongRole_IsForbiddenWithNoStateChange()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");

        var requestId = await CreateDraftRequestAsync(client, requestorToken);
        // "submitted" now automatically cascades all the way to ai_reviewed
        // (WorkflowAutomationService), so only two explicit calls are needed
        // to reach capacity_review.
        await TransitionAsync(client, requestorToken, requestId, "submitted");
        await TransitionAsync(client, requestorToken, requestId, "capacity_review");

        // capacity_review -> infra_approval requires CapacityManager; Requestor should be rejected.
        var response = await TransitionAsync(client, requestorToken, requestId, "infra_approval");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var getResponse = await GetRequestAsync(client, requestorToken, requestId);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CapacityReview", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task FullHappyPath_DraftToDone_Succeeds()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var capacityManagerToken = await GetAccessTokenAsync(client, "capacitymanager.dev");
        var infraHeadToken = await GetAccessTokenAsync(client, "infrahead.dev");

        var requestId = await CreateDraftRequestAsync(client, requestorToken);

        // "submitted" automatically cascades to ai_reviewed (WorkflowAutomationService).
        Assert.Equal(HttpStatusCode.OK, (await TransitionAsync(client, requestorToken, requestId, "submitted")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TransitionAsync(client, requestorToken, requestId, "capacity_review")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TransitionAsync(client, capacityManagerToken, requestId, "infra_approval")).StatusCode);

        var finalResponse = await TransitionAsync(client, infraHeadToken, requestId, "done");
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);

        var body = await finalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Done", body.GetProperty("status").GetString());

        var stageNames = body.GetProperty("workflowStages").EnumerateArray()
            .Select(s => s.GetProperty("stageName").GetString())
            .ToList();
        Assert.Equal(
            new[] { "draft", "submitted", "ai_evaluation", "ai_reviewed", "capacity_review", "infra_approval", "done" },
            stageNames);

        // Every stage in a fully-completed happy path closes out as Approved.
        var statuses = body.GetProperty("workflowStages").EnumerateArray()
            .Select(s => s.GetProperty("status").GetString())
            .ToList();
        Assert.All(statuses, s => Assert.Equal("Approved", s));

        // "done" auto-generates the Excel report and persists it as an
        // Attachment (WorkflowAutomationService) - confirm the row exists
        // and the file was actually written to disk.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
        var attachment = await db.Attachments.SingleAsync(a => a.RequestId == requestId);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", attachment.ContentType);
        Assert.True(File.Exists(attachment.StoragePath), $"Expected generated report file at {attachment.StoragePath}");
    }

    [Fact]
    public async Task Transition_CapacityReviewToRejected_ThenBlocksFurtherTransitions()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var capacityManagerToken = await GetAccessTokenAsync(client, "capacitymanager.dev");

        var requestId = await CreateDraftRequestAsync(client, requestorToken);
        await TransitionAsync(client, requestorToken, requestId, "submitted");
        await TransitionAsync(client, requestorToken, requestId, "capacity_review");

        var rejectResponse = await TransitionAsync(client, capacityManagerToken, requestId, "rejected");
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var body = await rejectResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Rejected", body.GetProperty("status").GetString());

        // rejected is terminal: no WorkflowConfig row to transition from.
        var furtherAttempt = await TransitionAsync(client, capacityManagerToken, requestId, "done");
        Assert.Equal(HttpStatusCode.Conflict, furtherAttempt.StatusCode);
    }

    [Fact]
    public async Task Transition_DraftToSubmitted_ByNonOwnerNonAdmin_IsForbiddenWithNoStateChange()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var requestId = await CreateDraftRequestAsync(client, requestorToken);

        // infrahead.dev has no relationship to this request and isn't Admin —
        // "draft" has no RequiredRole, but that must mean "owner or Admin
        // only", not "any authenticated user".
        var infraHeadToken = await GetAccessTokenAsync(client, "infrahead.dev");
        var response = await TransitionAsync(client, infraHeadToken, requestId, "submitted");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var getResponse = await GetRequestAsync(client, requestorToken, requestId);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("workflowStages").EnumerateArray());
    }

    [Fact]
    public async Task ConcurrentTransitions_OnSameRequest_SecondOneFailsWithConflict()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var requestId = await CreateDraftRequestAsync(client, requestorToken);

        // Simulates two actors who both read the request before either
        // commits: dbA tracks it at ConcurrencyVersion 0 and never re-reads
        // that scalar from the DB, even though a later query on dbA still
        // performs Include-based fixup for related WorkflowStages.
        using var scopeA = _factory.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<CapacityDbContext>();
        var trackedInA = await dbA.Requests.FirstAsync(r => r.Id == requestId);

        using var scopeB = _factory.Services.CreateScope();
        var dbB = scopeB.ServiceProvider.GetRequiredService<CapacityDbContext>();
        var automationB = scopeB.ServiceProvider.GetRequiredService<IWorkflowAutomationService>();
        var engineB = new WorkflowEngine(dbB, automationB);
        var firstResult = await engineB.TransitionAsync(
            requestId, "submitted", trackedInA.RequestorUserId, UserRole.Requestor, null);
        Assert.Equal(WorkflowTransitionOutcome.Success, firstResult.Outcome);

        // dbA's tracked Request is now stale (DB is at ConcurrencyVersion 1,
        // dbA still has 0) — this transition must be rejected, not silently
        // overwrite engineB's committed change.
        var automationA = scopeA.ServiceProvider.GetRequiredService<IWorkflowAutomationService>();
        var engineA = new WorkflowEngine(dbA, automationA);
        var secondResult = await engineA.TransitionAsync(
            requestId, "submitted", trackedInA.RequestorUserId, UserRole.Requestor, null);

        Assert.Equal(WorkflowTransitionOutcome.IllegalTransition, secondResult.Outcome);
        Assert.Contains("concurrently", secondResult.FailureReason);

        // Confirm only ONE "submitted" stage row exists — the second,
        // conflicting call did not sneak through a duplicate.
        var submittedStageCount = await dbB.WorkflowStages
            .CountAsync(ws => ws.RequestId == requestId && ws.StageName == "submitted");
        Assert.Equal(1, submittedStageCount);
    }

    private static async Task<int> CreateDraftRequestAsync(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/v1/requests", TestRequestPayloads.ValidCreateRequest());
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static Task<HttpResponseMessage> TransitionAsync(
        HttpClient client, string token, int requestId, string targetStage, string? comments = null)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.PostAsJsonAsync($"/api/v1/requests/{requestId}/transition", new { targetStage, comments });
    }

    private static Task<HttpResponseMessage> GetRequestAsync(HttpClient client, string token, int requestId)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync($"/api/v1/requests/{requestId}");
    }

    private async Task AssertAuditLogWrittenAsync(int requestId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
        var hasAuditLog = await db.AuditLogs.AnyAsync(a =>
            a.EntityType == "Request" && a.EntityId == requestId && a.Action == "WorkflowTransition");
        Assert.True(hasAuditLog);
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
