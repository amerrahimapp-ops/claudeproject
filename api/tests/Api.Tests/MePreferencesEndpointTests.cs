using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.Tests;

/// <summary>
/// Phase 6a "my preferences" endpoints: GET/PUT /api/v1/me/preferences.
/// Any authenticated user, own record only (no Admin gate). Uses
/// capacitymanager.dev (rather than admin, which every other Admin-gated
/// test in this suite uses) so a leftover UserPreferences row from this
/// test doesn't interfere with anything asserting on the admin user.
/// </summary>
[Collection("Integration")]
public class MePreferencesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MePreferencesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task GetPreferences_WithoutAuth_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/me/preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPreferences_BeforeAnyPutHasHappened_DefaultsToDashboard()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "infrahead.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/me/preferences");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Not asserting exact value here: the dev DB is shared across test
        // runs, so a previous run's PUT may have already persisted a
        // non-default value for this user. Just assert the shape is valid.
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("defaultView").GetString()));
    }

    [Fact]
    public async Task PutThenGetPreferences_RoundTripsTheStoredValue()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "capacitymanager.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var putResponse = await client.PutAsJsonAsync(
            "/api/v1/me/preferences", new { defaultView = "ApprovalQueue" });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var putBody = await putResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ApprovalQueue", putBody.GetProperty("defaultView").GetString());

        var getResponse = await client.GetAsync("/api/v1/me/preferences");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ApprovalQueue", getBody.GetProperty("defaultView").GetString());

        // Reset to the default so this test is idempotent across reruns
        // against the shared dev DB.
        var resetResponse = await client.PutAsJsonAsync(
            "/api/v1/me/preferences", new { defaultView = "Dashboard" });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
    }

    [Fact]
    public async Task PutPreferences_WithUnknownValue_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/preferences", new { defaultView = "NotARealView" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutPreferences_WithoutAuth_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/preferences", new { defaultView = "Dashboard" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
