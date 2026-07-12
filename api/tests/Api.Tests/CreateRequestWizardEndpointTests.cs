using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.Tests;

/// <summary>
/// Phase 7a: the expanded create-request payload (title/department/
/// projectName/projectCode/sponsor/dates/description, resources, servers,
/// justifications — see api/src/Api/Modules/Requests/RequestsDtos.cs).
/// Covers the full round-trip, server-computed uplift %, and validation
/// errors for bad input. Same pattern as HealthAndRequestsEndpointTests.cs.
///
/// Prerequisite: `docker compose up -d mysql` from the repo root.
/// </summary>
[Collection("Integration")]
public class CreateRequestWizardEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CreateRequestWizardEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task CreateRequest_WithFullPayload_RoundTripsEveryField()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            title = "Payment Gateway Capacity Uplift",
            department = "Payments Engineering",
            projectName = "Payment Gateway",
            projectCode = "PAY-042",
            sponsor = "Jane Sponsor",
            environment = "Prod",
            projectType = "Enhancement",
            priority = "High",
            startDate = "2026-08-01T00:00:00Z",
            endDate = "2026-09-30T00:00:00Z",
            description = "Uplift storage ahead of Q4 peak load.",
            resources = new[]
            {
                new { resourceType = "Storage", currentValue = 200, requestedValue = 250 },
            },
            servers = new[]
            {
                new
                {
                    hostname = "app01",
                    ipAddress = "10.0.0.5",
                    os = "RHEL 8.6",
                    isPhysical = false,
                    resourceType = "Storage",
                    currentValue = 200,
                    requestedValue = 250,
                    mountPoint = "/data",
                    platform = "Unix",
                    drApplicable = true,
                    appTier = "Tier 1",
                },
            },
            justifications = new[]
            {
                new { resourceType = "Storage", questionKey = "data_lifecycle", answerText = "Retained for 90 days." },
            },
        };

        var createResponse = await client.PostAsJsonAsync("/api/v1/requests", payload);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = created.GetProperty("id").GetInt32();

        var getResponse = await client.GetAsync($"/api/v1/requests/{requestId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Payment Gateway Capacity Uplift", body.GetProperty("title").GetString());
        Assert.Equal("Payments Engineering", body.GetProperty("department").GetString());
        Assert.Equal("Payment Gateway", body.GetProperty("projectName").GetString());
        Assert.Equal("PAY-042", body.GetProperty("projectCode").GetString());
        Assert.Equal("Jane Sponsor", body.GetProperty("sponsor").GetString());
        Assert.Equal("Enhancement", body.GetProperty("projectType").GetString());
        Assert.Equal("High", body.GetProperty("priority").GetString());
        Assert.Equal("Uplift storage ahead of Q4 peak load.", body.GetProperty("description").GetString());
        Assert.Equal("requestor.dev", body.GetProperty("requestorUsername").GetString());

        var resources = body.GetProperty("resources").EnumerateArray().ToList();
        Assert.Single(resources);
        Assert.Equal("Storage", resources[0].GetProperty("resourceType").GetString());
        Assert.Equal(200, resources[0].GetProperty("currentValue").GetDecimal());
        Assert.Equal(250, resources[0].GetProperty("requestedValue").GetDecimal());
        // (250 - 200) / 200 * 100 == 25 — computed server-side, never trusted from the client
        // (the request payload above never sends an upliftPercent field at all).
        Assert.Equal(25m, resources[0].GetProperty("upliftPercent").GetDecimal());

        var servers = body.GetProperty("servers").EnumerateArray().ToList();
        Assert.Single(servers);
        Assert.Equal("app01", servers[0].GetProperty("hostname").GetString());
        Assert.Equal("10.0.0.5", servers[0].GetProperty("ipAddress").GetString());
        Assert.Equal("RHEL 8.6", servers[0].GetProperty("os").GetString());
        Assert.False(servers[0].GetProperty("isPhysical").GetBoolean());
        Assert.Equal("/data", servers[0].GetProperty("mountPoint").GetString());
        Assert.Equal("Unix", servers[0].GetProperty("platform").GetString());
        Assert.True(servers[0].GetProperty("drApplicable").GetBoolean());
        Assert.Equal("Tier 1", servers[0].GetProperty("appTier").GetString());

        var justifications = body.GetProperty("justifications").EnumerateArray().ToList();
        Assert.Single(justifications);
        Assert.Equal("data_lifecycle", justifications[0].GetProperty("questionKey").GetString());
        Assert.Equal("Retained for 90 days.", justifications[0].GetProperty("answerText").GetString());
    }

    [Fact]
    public async Task CreateRequest_WithEmptyResources_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/requests", new
        {
            title = "No Resources",
            department = "IT",
            projectName = "Test",
            projectCode = "T-1",
            sponsor = "Sponsor",
            environment = "Prod",
            projectType = "New",
            priority = "Low",
            startDate = "2026-08-01T00:00:00Z",
            endDate = "2026-09-01T00:00:00Z",
            resources = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRequest_WithEndDateBeforeStartDate_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/requests", new
        {
            title = "Backwards Dates",
            department = "IT",
            projectName = "Test",
            projectCode = "T-2",
            sponsor = "Sponsor",
            environment = "Prod",
            projectType = "New",
            priority = "Low",
            startDate = "2026-09-01T00:00:00Z",
            endDate = "2026-08-01T00:00:00Z",
            resources = new[] { new { resourceType = "Storage", currentValue = 10, requestedValue = 20 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRequest_WithUnknownResourceType_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/requests", new
        {
            title = "Bogus Resource",
            department = "IT",
            projectName = "Test",
            projectCode = "T-3",
            sponsor = "Sponsor",
            environment = "Prod",
            projectType = "New",
            priority = "Low",
            startDate = "2026-08-01T00:00:00Z",
            endDate = "2026-09-01T00:00:00Z",
            resources = new[] { new { resourceType = "Bogus", currentValue = 10, requestedValue = 20 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRequest_WithNegativeRequestedValue_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/requests", new
        {
            title = "Negative Value",
            department = "IT",
            projectName = "Test",
            projectCode = "T-4",
            sponsor = "Sponsor",
            environment = "Prod",
            projectType = "New",
            priority = "Low",
            startDate = "2026-08-01T00:00:00Z",
            endDate = "2026-09-01T00:00:00Z",
            resources = new[] { new { resourceType = "Storage", currentValue = 10, requestedValue = -5 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
