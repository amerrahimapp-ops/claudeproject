using Api.Data;
using Api.Data.Entities;
using Api.Modules.Integrations.Email;
using Api.Modules.Integrations.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Tests;

/// <summary>
/// Exercises the async outbox pattern end to end (design spec Section 2.3):
/// IOutboxWriter enqueues a Pending row, and the OutboxProcessor background
/// service picks it up and delivers it via IEmailClient without the caller
/// ever touching the email client directly.
/// </summary>
[Collection("Integration")]
public class OutboxTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OutboxTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            // Force Mock regardless of local appsettings.Development.json
            // (which may point at real Mailtrap credentials). The
            // Email:Provider config override alone (as used by
            // EmailEndpointTests/GrafanaEndpointTests) isn't reliable enough
            // here: this test asserts on the concrete IEmailClient instance,
            // and observed that a plain config override can still lose the
            // race to appsettings.Development.json's real "Mailtrap" value
            // depending on config-provider ordering. Overriding the DI
            // registration directly after AddIntegrationsModule has run
            // guarantees the mock is what's actually resolved.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Provider"] = "Mock",
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailClient>();
                services.AddSingleton<IEmailClient, MockEmailClient>();
            });
        });
    }

    [Fact]
    public async Task EnqueueEmail_IsPersistedAsPending_ThenDeliveredByProcessor()
    {
        // Creating a client spins up the host (and the OutboxProcessor
        // hosted service) via the shared WebApplicationFactory.
        using var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();

        var toAddress = $"outbox-test-{Guid.NewGuid():N}@example.com";
        const string subject = "Outbox test subject";
        const string body = "Outbox test body";

        await outboxWriter.EnqueueEmailAsync(toAddress, subject, body);

        // Payload is a JSON column - Pomelo translates a naive
        // Payload.Contains(...) filter into a MySQL JSON-cast on the
        // parameter, which then rejects the plain string. Simplest reliable
        // way to find "the row we just wrote" is by CreatedAt/Id ordering.
        var pending = await db.OutboxMessages
            .Where(m => m.MessageType == "Email")
            .OrderByDescending(m => m.Id)
            .FirstAsync();

        Assert.Equal(OutboxMessageStatus.Pending, pending.Status);
        Assert.Equal(0, pending.Attempts);

        // Poll for the background processor to pick up and deliver the
        // message - default poll interval is short (see OutboxOptions),
        // comfortably covered by this timeout.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        OutboxMessage? delivered = null;
        while (DateTime.UtcNow < deadline)
        {
            using var pollScope = _factory.Services.CreateScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<CapacityDbContext>();
            delivered = await pollDb.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == pending.Id);

            if (delivered.Status is OutboxMessageStatus.Sent or OutboxMessageStatus.Failed)
            {
                break;
            }

            await Task.Delay(500);
        }

        Assert.NotNull(delivered);
        Assert.Equal(OutboxMessageStatus.Sent, delivered!.Status);
        Assert.NotNull(delivered.ProcessedAt);

        // The mock email client is registered as a singleton, so its
        // captured last-call state proves the processor actually invoked it
        // (rather than e.g. just flipping the row's status).
        var emailClient = Assert.IsType<MockEmailClient>(_factory.Services.GetRequiredService<IEmailClient>());
        Assert.True(emailClient.SentCount > 0);
        Assert.Equal(toAddress, emailClient.LastToAddress);
        Assert.Equal(subject, emailClient.LastSubject);
        Assert.Equal(body, emailClient.LastBody);
    }
}
