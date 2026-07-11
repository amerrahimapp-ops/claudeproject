using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Api.Tests;

/// <summary>
/// Phase 4 test-grafana endpoint: confirms the Admin-only auth guard works
/// and, with the default Mock Grafana provider (see appsettings.json), that
/// a request succeeds without needing real Grafana credentials.
/// </summary>
[Collection("Integration")]
public class GrafanaEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GrafanaEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            // Force Mock regardless of what a developer's local
            // appsettings.Development.json has configured (e.g. real
            // Grafana credentials) - these tests must always exercise the
            // mock path, not silently hit a live provider.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Grafana:Provider"] = "Mock",
                });
            });
        });
    }

    [Fact]
    public async Task TestGrafana_AsAdmin_WithMockProvider_Succeeds()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/admin/test-grafana?query=up");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", body);
    }

    [Fact]
    public async Task TestGrafana_AsNonAdmin_IsForbidden()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/admin/test-grafana?query=up");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TestGrafana_WithoutAuth_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/test-grafana?query=up");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
