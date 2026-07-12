using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

/// <summary>
/// Phase 8c attachment upload: POST/GET/GET-by-id under
/// <c>/api/v1/requests/{id}/attachments</c>. Same WebApplicationFactory +
/// real local MySQL integration-test pattern as ReportEndpointTests, whose
/// file-download assertions (Results.File content-type/filename) this
/// mirrors for the download endpoint.
///
/// Prerequisite: `docker compose up -d mysql` from the repo root.
/// </summary>
[Collection("Integration")]
public class AttachmentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AttachmentEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task UploadThenListThenDownload_RoundTripsFileContentAndMetadata()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestId = await CreateDraftRequestAsync(client);

        var fileBytes = Encoding.UTF8.GetBytes("hello attachment world");
        using var uploadContent = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(fileContent, "file", "notes.txt");

        var uploadResponse = await client.PostAsync($"/api/v1/requests/{requestId}/attachments", uploadContent);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("notes.txt", uploaded.GetProperty("fileName").GetString());
        var attachmentId = uploaded.GetProperty("id").GetInt32();

        var listResponse = await client.GetAsync($"/api/v1/requests/{requestId}/attachments");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(list.EnumerateArray());
        Assert.Equal("notes.txt", list[0].GetProperty("fileName").GetString());
        Assert.Equal("Dev Requestor", list[0].GetProperty("uploadedByDisplayName").GetString());

        var downloadResponse = await client.GetAsync($"/api/v1/requests/{requestId}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("text/plain", downloadResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("notes.txt", downloadResponse.Content.Headers.ContentDisposition?.FileNameStar
            ?? downloadResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(fileBytes, downloadedBytes);

        // Confirm the file was actually persisted to disk under
        // request-attachments/{requestId}/, matching the storage-path
        // convention (see RequestsEndpoints.cs / .gitignore).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
        var attachment = await db.Attachments.SingleAsync(a => a.Id == attachmentId);
        Assert.True(File.Exists(attachment.StoragePath));
        Assert.Contains(Path.Combine("request-attachments", requestId.ToString()), attachment.StoragePath);
    }

    [Fact]
    public async Task Upload_OversizedFile_IsRejected()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestId = await CreateDraftRequestAsync(client);

        var oversized = new byte[11 * 1024 * 1024]; // 11MB > 10MB limit
        using var uploadContent = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(oversized);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(fileContent, "file", "big.txt");

        var response = await client.PostAsync($"/api/v1/requests/{requestId}/attachments", uploadContent);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_DisallowedExtension_IsRejected()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestId = await CreateDraftRequestAsync(client);

        using var uploadContent = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("MZ fake exe"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        uploadContent.Add(fileContent, "file", "malware.exe");

        var response = await client.PostAsync($"/api/v1/requests/{requestId}/attachments", uploadContent);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ByNonOwnerNonAdmin_IsForbidden()
    {
        var client = _factory.CreateClient();
        var ownerToken = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var requestId = await CreateDraftRequestAsync(client);

        var infraHeadToken = await GetAccessTokenAsync(client, "infrahead.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", infraHeadToken);

        using var uploadContent = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hi"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(fileContent, "file", "notes.txt");

        var response = await client.PostAsync($"/api/v1/requests/{requestId}/attachments", uploadContent);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ByAdmin_ForSomeoneElsesRequest_Succeeds()
    {
        var client = _factory.CreateClient();
        var ownerToken = await GetAccessTokenAsync(client, "requestor.dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var requestId = await CreateDraftRequestAsync(client);

        var adminToken = await GetAccessTokenAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        using var uploadContent = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("admin upload"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(fileContent, "file", "admin-notes.txt");

        var response = await client.PostAsync($"/api/v1/requests/{requestId}/attachments", uploadContent);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetAttachments_WithoutAuth_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/requests/1/attachments");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<int> CreateDraftRequestAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/requests", TestRequestPayloads.ValidCreateRequest());
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "anything" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
