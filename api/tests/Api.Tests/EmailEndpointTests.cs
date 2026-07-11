using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Modules.Integrations.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Tests;

/// <summary>
/// Phase 4 test-email endpoint: confirms the Admin-only auth guard actually
/// works and, with the default Mock email provider (see appsettings.json),
/// that a request succeeds without needing real Mailtrap credentials.
/// </summary>
[Collection("Integration")]
public class EmailEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EmailEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            // Force Mock regardless of what a developer's local
            // appsettings.Development.json has configured (e.g. real
            // Mailtrap credentials). A config-key override
            // (ConfigureAppConfiguration) is NOT reliable here - the
            // Provider switch is read once in Program.cs's AddIntegrationsModule
            // call during host build, and config added via
            // ConfigureAppConfiguration doesn't consistently win that race.
            // Replacing the DI registration directly is deterministic.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailClient>();
                services.AddSingleton<IEmailClient, MockEmailClient>();
            });
        });
    }

    [Fact]
    public async Task TestEmail_AsAdmin_WithMockProvider_Succeeds()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/test-email", new { toAddress = "test@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("sent").GetBoolean());
    }

    [Fact]
    public async Task TestEmail_AsNonAdmin_IsForbidden()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/test-email", new { toAddress = "test@example.com" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TestEmail_WithoutAuth_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/test-email", new { toAddress = "test@example.com" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
