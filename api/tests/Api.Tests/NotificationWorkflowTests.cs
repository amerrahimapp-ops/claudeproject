using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
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
/// Phase 8b: real workflow notifications (spec 4.4/10.5), enqueued through
/// IOutboxWriter and asserted directly against the OutboxMessages table
/// (delivery itself is OutboxProcessor's job, already covered by its own
/// pattern - not re-tested here). Same WebApplicationFactory + real local
/// MySQL integration-test pattern as WorkflowEngineTests, with the same
/// Mock AI/Grafana override so the automatic ai_evaluation cascade is
/// deterministic.
///
/// Every test creates its own request with a distinctive title/uses the
/// request's unique RequestNumber to scope its OutboxMessages queries, so
/// tests don't see each other's enqueued emails on the shared dev DB.
///
/// Prerequisite: `docker compose up -d mysql` from the repo root.
/// </summary>
[Collection("Integration")]
public class NotificationWorkflowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NotificationWorkflowTests(WebApplicationFactory<Program> factory)
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
    public async Task SubmittingRequest_EnqueuesExactlyOneStatusChangedEmail_ForTheSettledStage()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var requestId = await CreateDraftRequestAsync(client, requestorToken);
        var requestNumber = await GetRequestNumberAsync(requestId);

        // "submitted" cascades through ai_evaluation to ai_reviewed within
        // this single call (WorkflowAutomationService) - only the final
        // settled stage should produce a notification, not one per hop.
        var response = await TransitionAsync(client, requestorToken, requestId, "submitted");
        response.EnsureSuccessStatusCode();

        var emails = await GetOutboxEmailsForRequestAsync(requestNumber);

        // ai_reviewed has no RequiredRole, so the only expected email is the
        // requestor's status-changed notification - a single email for what
        // was, from the requestor's perspective, one Submit click.
        var email = Assert.Single(emails);
        Assert.Equal("requestor.dev@dev.local", email.ToAddress);
        Assert.Contains(requestNumber, email.Subject);
        Assert.Contains("status changed to Ai Reviewed", email.Subject);
        // Reports the cascade's true origin (Draft), not an intermediate
        // hop's own before/after (e.g. "AiEvaluation -> AiReviewed").
        Assert.Contains("Draft -> Ai Reviewed", email.Body);
    }

    [Fact]
    public async Task AiEvaluationFailure_SettlesAtAiEvaluation_AndStillNotifiesOnce()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiEvaluationClient>();
                services.AddSingleton<IAiEvaluationClient, FailingAiEvaluationClient>();
                services.RemoveAll<IGrafanaClient>();
                services.AddSingleton<IGrafanaClient, MockGrafanaClient>();
            });
        });

        var client = factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var requestId = await CreateDraftRequestAsync(client, requestorToken);
        var requestNumber = await GetRequestNumberAsync(requestId);

        var response = await TransitionAsync(client, requestorToken, requestId, "submitted");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Genuinely stuck at ai_evaluation (no cascade further) when the AI
        // evaluation call fails - this settled state is worth notifying
        // about even though it isn't the "happy path" final stage.
        Assert.Equal("AiEvaluation", body.GetProperty("status").GetString());

        var emails = await GetOutboxEmailsForRequestAsync(requestNumber);
        var email = Assert.Single(emails);
        Assert.Equal("requestor.dev@dev.local", email.ToAddress);
        Assert.Contains("status changed to Ai Evaluation", email.Subject);
        Assert.Contains("Draft -> Ai Evaluation", email.Body);
    }

    [Fact]
    public async Task ManualTransitionIntoStageWithRequiredRole_NotifiesRequestorAndRole()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var requestId = await CreateDraftRequestAsync(client, requestorToken);
        var requestNumber = await GetRequestNumberAsync(requestId);

        // draft -> submitted cascades to ai_reviewed (one email, covered by
        // the test above); this test's interest is the next, manual hop.
        (await TransitionAsync(client, requestorToken, requestId, "submitted")).EnsureSuccessStatusCode();
        var response = await TransitionAsync(client, requestorToken, requestId, "capacity_review");
        response.EnsureSuccessStatusCode();

        var emails = await GetOutboxEmailsForRequestAsync(requestNumber);

        // One status-changed email to the requestor (AiReviewed -> CapacityReview)...
        var statusEmail = Assert.Single(emails, e => e.ToAddress == "requestor.dev@dev.local" && e.Subject.Contains("status changed to Capacity Review"));
        Assert.Contains("Ai Reviewed -> Capacity Review", statusEmail.Body);

        // ...plus a new-task email to capacitymanager.dev, because
        // capacity_review's WorkflowConfig.RequiredRole is CapacityManager.
        var roleEmail = Assert.Single(emails, e => e.ToAddress == "capacitymanager.dev@dev.local");
        Assert.Contains("new task waiting in Capacity Review", roleEmail.Subject);

        // Exactly these two (plus the one from the submit cascade) - nothing
        // extra snuck in.
        Assert.Equal(3, emails.Count);
    }

    [Fact]
    public async Task Transition_RespectsRequestStatusChangedOptOut()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");

        await SetNotificationPrefsAsync(client, requestorToken, requestStatusChanged: false, newAssignedTask: true);
        try
        {
            var requestId = await CreateDraftRequestAsync(client, requestorToken);
            var requestNumber = await GetRequestNumberAsync(requestId);

            (await TransitionAsync(client, requestorToken, requestId, "submitted")).EnsureSuccessStatusCode();

            var emails = await GetOutboxEmailsForRequestAsync(requestNumber);
            Assert.Empty(emails);
        }
        finally
        {
            // Reset so this test is idempotent across reruns against the
            // shared dev DB (same convention as MePreferencesEndpointTests).
            await SetNotificationPrefsAsync(client, requestorToken, requestStatusChanged: true, newAssignedTask: true);
        }
    }

    [Fact]
    public async Task Transition_RespectsNewAssignedTaskOptOut()
    {
        var client = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(client, "requestor.dev");
        var capacityManagerToken = await GetAccessTokenAsync(client, "capacitymanager.dev");

        await SetNotificationPrefsAsync(client, capacityManagerToken, requestStatusChanged: true, newAssignedTask: false);
        try
        {
            var requestId = await CreateDraftRequestAsync(client, requestorToken);
            var requestNumber = await GetRequestNumberAsync(requestId);

            (await TransitionAsync(client, requestorToken, requestId, "submitted")).EnsureSuccessStatusCode();
            (await TransitionAsync(client, requestorToken, requestId, "capacity_review")).EnsureSuccessStatusCode();

            var emails = await GetOutboxEmailsForRequestAsync(requestNumber);

            // The requestor's own status-changed emails still fire...
            Assert.Contains(emails, e => e.ToAddress == "requestor.dev@dev.local" && e.Subject.Contains("Capacity Review"));
            // ...but capacitymanager.dev opted out of new-task emails, so
            // none was enqueued for them even though capacity_review's
            // RequiredRole is CapacityManager.
            Assert.DoesNotContain(emails, e => e.ToAddress == "capacitymanager.dev@dev.local");
        }
        finally
        {
            await SetNotificationPrefsAsync(client, capacityManagerToken, requestStatusChanged: true, newAssignedTask: true);
        }
    }

    private async Task SetNotificationPrefsAsync(HttpClient client, string token, bool requestStatusChanged, bool newAssignedTask)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PutAsJsonAsync(
            "/api/v1/me/preferences",
            new
            {
                defaultView = "Dashboard",
                notificationPrefs = new { requestStatusChanged, newAssignedTask },
            });
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetRequestNumberAsync(int requestId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
        var request = await db.Requests.AsNoTracking().FirstAsync(r => r.Id == requestId);
        return request.RequestNumber;
    }

    private async Task<List<(string ToAddress, string Subject, string Body)>> GetOutboxEmailsForRequestAsync(string requestNumber)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
        // Payload is a MySQL `json` column - both a plain .Contains(...) and
        // EF.Functions.Like(...) get translated into JSON-aware functions
        // (JSON_CONTAINS / a JSON cast) that require their argument to
        // itself be valid JSON and throw on a bare string. Filter client-side
        // instead: fetch all Email messages and match the request number as
        // plain text.
        var messages = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.MessageType == "Email")
            .ToListAsync();

        return messages
            .Where(m => m.Payload.Contains(requestNumber, StringComparison.Ordinal))
            .Select(m => JsonSerializer.Deserialize<JsonElement>(m.Payload))
            .Select(p => (
                p.GetProperty("toAddress").GetString()!,
                p.GetProperty("subject").GetString()!,
                p.GetProperty("body").GetString()!))
            .ToList();
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

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}

/// <summary>
/// Test double that always fails, to exercise the "AI evaluation failed, so
/// the request settles at ai_evaluation instead of cascading to ai_reviewed"
/// branch (WorkflowAutomationService) and its notification.
/// </summary>
public class FailingAiEvaluationClient : IAiEvaluationClient
{
    public Task<AiEvaluationResult> EvaluateAsync(AiEvaluationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiEvaluationResult(false, null, "[prompt]", "", "Simulated failure for NotificationWorkflowTests."));
}
