using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

/// <summary>
/// Phase 6a audit log viewer endpoint: confirms the Admin-only auth guard
/// works and that pagination/filtering behave against real rows. Nothing
/// currently writes AuditLogs rows for the entity type used here
/// (WorkflowEngine.TransitionAsync is the only current writer, and it always
/// logs EntityType "Request") — so, per the task brief, this test seeds its
/// own rows directly via CapacityDbContext with a unique per-run EntityType
/// to avoid colliding with any other data in the shared dev MySQL database.
/// </summary>
[Collection("Integration")]
public class AdminAuditLogEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _entityType = $"TestEntity-{Guid.NewGuid():N}";

    public AdminAuditLogEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
    }

    private async Task SeedAuditLogRowsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();

        var now = DateTime.UtcNow;
        db.AuditLogs.AddRange(
            new AuditLog
            {
                EntityType = _entityType,
                EntityId = 1,
                Action = "Created",
                OldValues = null,
                NewValues = """{"status":"Draft"}""",
                PerformedByUserId = 1, // seeded "admin" dev user
                PerformedAt = now.AddMinutes(-10),
            },
            new AuditLog
            {
                EntityType = _entityType,
                EntityId = 1,
                Action = "Updated",
                OldValues = """{"status":"Draft"}""",
                NewValues = """{"status":"Submitted"}""",
                PerformedByUserId = 1,
                PerformedAt = now.AddMinutes(-5),
            },
            new AuditLog
            {
                EntityType = _entityType,
                EntityId = 2,
                Action = "Created",
                OldValues = null,
                NewValues = """{"status":"Draft"}""",
                PerformedByUserId = 2, // seeded "requestor.dev" user
                PerformedAt = now,
            });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAuditLog_AsAdmin_ReturnsSeededRowsNewestFirstWithDisplayName()
    {
        await SeedAuditLogRowsAsync();

        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/admin/audit-log?entityType={_entityType}&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);

        // Newest first.
        Assert.Equal("Created", items[0].GetProperty("action").GetString());
        Assert.Equal(2, items[0].GetProperty("entityId").GetInt32());
        Assert.Equal("Dev Requestor", items[0].GetProperty("performedByUserName").GetString());

        Assert.Equal("Updated", items[1].GetProperty("action").GetString());
        Assert.Equal("Local Admin", items[1].GetProperty("performedByUserName").GetString());
    }

    [Fact]
    public async Task GetAuditLog_AsAdmin_FiltersByEntityIdAndAction()
    {
        await SeedAuditLogRowsAsync();

        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(
            $"/api/v1/admin/audit-log?entityType={_entityType}&entityId=1&action=Updated");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Updated", items[0].GetProperty("action").GetString());
        Assert.Equal(1, items[0].GetProperty("entityId").GetInt32());
    }

    [Fact]
    public async Task GetAuditLog_AsAdmin_RespectsPagination()
    {
        await SeedAuditLogRowsAsync();

        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(
            $"/api/v1/admin/audit-log?entityType={_entityType}&page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, body.GetProperty("items").EnumerateArray().Count());
    }

    [Fact]
    public async Task GetAuditLog_AsNonAdmin_IsForbidden()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/admin/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditLog_WithoutAuth_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit-log");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
