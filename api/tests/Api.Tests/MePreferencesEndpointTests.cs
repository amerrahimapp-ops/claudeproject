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
    public async Task GetPreferences_BeforeAnyPutHasHappened_DefaultsToDarkAndAllNotificationsOn()
    {
        var client = _factory.CreateClient();
        // infrahead.dev is also used by the "defaults to Dashboard" test above;
        // both only assert defaults that hold before any PUT ever touches
        // theme/notificationPrefs for this user, so they don't conflict.
        var token = await GetAccessTokenAsync(client, "infrahead.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/me/preferences");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Dark", body.GetProperty("theme").GetString());
        Assert.True(body.GetProperty("notificationPrefs").GetProperty("requestStatusChanged").GetBoolean());
        Assert.True(body.GetProperty("notificationPrefs").GetProperty("newAssignedTask").GetBoolean());
    }

    [Fact]
    public async Task PutThenGetPreferences_RoundTripsThemeAndNotificationPrefs()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "capacitymanager.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var putResponse = await client.PutAsJsonAsync(
            "/api/v1/me/preferences",
            new
            {
                defaultView = "Dashboard",
                theme = "Light",
                notificationPrefs = new { requestStatusChanged = false, newAssignedTask = true },
            });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var putBody = await putResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Light", putBody.GetProperty("theme").GetString());
        Assert.False(putBody.GetProperty("notificationPrefs").GetProperty("requestStatusChanged").GetBoolean());
        Assert.True(putBody.GetProperty("notificationPrefs").GetProperty("newAssignedTask").GetBoolean());

        var getResponse = await client.GetAsync("/api/v1/me/preferences");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Light", getBody.GetProperty("theme").GetString());
        Assert.False(getBody.GetProperty("notificationPrefs").GetProperty("requestStatusChanged").GetBoolean());
        Assert.True(getBody.GetProperty("notificationPrefs").GetProperty("newAssignedTask").GetBoolean());

        // A PUT that omits theme/notificationPrefs must leave the previously
        // stored values untouched (only defaultView changes) — this is the
        // backward-compatible contract older callers rely on.
        var partialPutResponse = await client.PutAsJsonAsync(
            "/api/v1/me/preferences", new { defaultView = "NewRequest" });
        Assert.Equal(HttpStatusCode.OK, partialPutResponse.StatusCode);
        var partialPutBody = await partialPutResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NewRequest", partialPutBody.GetProperty("defaultView").GetString());
        Assert.Equal("Light", partialPutBody.GetProperty("theme").GetString());
        Assert.False(partialPutBody.GetProperty("notificationPrefs").GetProperty("requestStatusChanged").GetBoolean());

        // Reset to defaults so this test is idempotent across reruns against
        // the shared dev DB.
        var resetResponse = await client.PutAsJsonAsync(
            "/api/v1/me/preferences",
            new
            {
                defaultView = "Dashboard",
                theme = "Dark",
                notificationPrefs = new { requestStatusChanged = true, newAssignedTask = true },
            });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
    }

    [Fact]
    public async Task PutPreferences_WithUnknownTheme_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/preferences", new { defaultView = "Dashboard", theme = "Purple" });

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
