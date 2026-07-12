using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.Tests;

/// <summary>
/// Foundation-phase end-to-end smoke test: boots the real API host against
/// the real local MySQL (docker-compose "mysql" service), applies EF Core
/// migrations + seed data on startup (Program.cs, Development branch), then
/// hits real endpoints. This proves the EF Core + API + MySQL pipeline
/// works end-to-end and establishes the pattern later phases follow for
/// their own integration tests.
///
/// Prerequisite: `docker compose up -d mysql` from the repo root must be
/// running before this test executes.
/// </summary>
[Collection("Integration")]
public class HealthAndRequestsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthAndRequestsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task Health_ReturnsHealthyWithDatabaseConnected()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
        Assert.Contains("connected", body);
    }

    [Fact]
    public async Task GetRequests_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/requests");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRequests_WithAuth_ReturnsJsonArray()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Don't assert the list is empty — the dev DB is shared across test
        // runs, so only assert the shape (a valid JSON array) is correct.
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task Login_WithMockProvider_ReturnsJwt()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "anything" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("accessToken", body);
    }

    [Fact]
    public async Task GetRequests_AsRequestor_OnlyReturnsOwnRequests()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create at least one request as requestor.dev, so the assertion
        // below has a guaranteed-own row to find even on a fresh DB.
        await client.PostAsJsonAsync(
            "/api/v1/requests",
            TestRequestPayloads.ValidCreateRequest(environment: "Dev", priority: "Low"));

        var response = await client.GetAsync("/api/v1/requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.EnumerateArray().ToList();

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Equal(2, item.GetProperty("requestorUserId").GetInt32()));
    }

    [Fact]
    public async Task GetRequests_AsAdmin_CanSeeRequestsOwnedByOtherUsers()
    {
        var requestorClient = _factory.CreateClient();
        var requestorToken = await GetAccessTokenAsync(requestorClient, "requestor.dev");
        requestorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requestorToken);
        await requestorClient.PostAsJsonAsync(
            "/api/v1/requests",
            TestRequestPayloads.ValidCreateRequest(environment: "Dev", priority: "Low"));

        var adminClient = _factory.CreateClient();
        var adminToken = await GetAccessTokenAsync(adminClient, "admin");
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await adminClient.GetAsync("/api/v1/requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.EnumerateArray().ToList();

        Assert.Contains(items, item => item.GetProperty("requestorUserId").GetInt32() == 2);
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username = "admin")
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
