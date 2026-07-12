using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Data;
using Api.Modules.Ai;
using Api.Modules.Integrations.Grafana;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Tests;

/// <summary>
/// Phase 4 Excel report endpoint, extended in Phase 7b for the real AI
/// Evaluation sheet. Drives a real request through one workflow transition
/// (via the public HTTP API, same pattern as WorkflowEngineTests) so there's
/// at least one WorkflowStage row to render, then downloads the report and
/// does a structural check on the returned workbook per ADR 0001 ("Excel
/// generator: snapshot tests") — asserting sheet names/counts and key
/// content rather than a fragile byte-for-byte comparison.
///
/// Forces Mock AI/Grafana providers (same reasoning as WorkflowEngineTests):
/// "submitted" now automatically triggers the AI evaluation chain.
///
/// Prerequisite: `docker compose up -d mysql` from the repo root.
/// </summary>
[Collection("Integration")]
public class ReportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReportEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task GetReport_ForRequestWithWorkflowStage_ReturnsXlsxWithExpectedStructure()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestId = await CreateDraftRequestAsync(client);
        var requestNumber = await GetRequestNumberAsync(client, requestId);

        var transitionResponse = await client.PostAsJsonAsync(
            $"/api/v1/requests/{requestId}/transition",
            new { targetStage = "submitted", comments = (string?)null });
        transitionResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/v1/requests/{requestId}/report");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        Assert.Equal(3, workbook.Worksheets.Count);
        var sheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();
        Assert.Equal(new[] { "Request Summary", "AI Evaluation Report", "Approval Chain" }, sheetNames);

        var summarySheet = workbook.Worksheet("Request Summary");
        var summaryText = string.Join(" ", summarySheet.RangeUsed()!.CellsUsed().Select(c => c.GetString()));
        Assert.Contains(requestNumber, summaryText);

        var approvalSheet = workbook.Worksheet("Approval Chain");
        var approvalUsedRange = approvalSheet.RangeUsed()!;
        Assert.True(approvalUsedRange.RowCount() > 1, "Approval Chain sheet should have at least one data row beyond the header.");

        // "submitted" auto-cascaded through ai_evaluation (WorkflowAutomationService),
        // so the AI Evaluation Report sheet should now have real Mock-provider
        // content instead of the old hardcoded placeholder.
        var aiSheet = workbook.Worksheet("AI Evaluation Report");
        var aiSheetText = string.Join(" ", aiSheet.RangeUsed()!.CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("No AI evaluation data available", aiSheetText);
        Assert.Contains("approve", aiSheetText);
        Assert.Contains("80", aiSheetText);
    }

    [Fact]
    public async Task GetReport_WithMultipleAiEvaluations_RendersAllNewestFirst()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestId = await CreateDraftRequestAsync(client);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
            db.AiEvaluations.AddRange(
                new Api.Data.Entities.AiEvaluation
                {
                    RequestId = requestId,
                    Prompt = "p1",
                    RawResponse = "r1",
                    Score = 40,
                    Recommendation = "challenge",
                    FlagsJson = """["over-provisioned"]""",
                    EvaluatedAt = DateTime.UtcNow.AddDays(-2),
                },
                new Api.Data.Entities.AiEvaluation
                {
                    RequestId = requestId,
                    Prompt = "p2",
                    RawResponse = "r2",
                    Score = 90,
                    Recommendation = "approve",
                    FlagsJson = "[]",
                    EvaluatedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/v1/requests/{requestId}/report");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var aiSheet = workbook.Worksheet("AI Evaluation Report");

        // Newest first: row 2 is the most recent evaluation (score 90, approve).
        Assert.Equal(90, aiSheet.Cell(2, 2).GetValue<double>());
        Assert.Equal("approve", aiSheet.Cell(2, 3).GetString());
        Assert.Equal(40, aiSheet.Cell(3, 2).GetValue<double>());
        Assert.Equal("challenge", aiSheet.Cell(3, 3).GetString());
        Assert.Contains("over-provisioned", aiSheet.Cell(3, 4).GetString());
    }

    [Fact]
    public async Task GetReport_WithoutAuth_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/requests/1/report");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<int> CreateDraftRequestAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/requests", TestRequestPayloads.ValidCreateRequest());
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<string> GetRequestNumberAsync(HttpClient client, int requestId)
    {
        var response = await client.GetAsync($"/api/v1/requests/{requestId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("requestNumber").GetString()!;
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
