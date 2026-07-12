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

        // EnqueueEmailAsync returns the new row's id, so this test can find
        // its own message unambiguously - this table is shared across the
        // whole [Collection("Integration")] test run (real MySQL, not
        // per-test isolated), and other tests/background workflow
        // automation can and do write rows around the same time, so "the
        // last Email row by Id" is not reliably this test's own row.
        var pendingId = await outboxWriter.EnqueueEmailAsync(toAddress, subject, body);

        var pending = await db.OutboxMessages.SingleAsync(m => m.Id == pendingId);

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

        // OutboxProcessor.DeliverAsync only sets Status to Sent (with
        // ProcessedAt stamped) after IEmailClient.SendAsync returns
        // successfully - see OutboxProcessor.cs. This DB-level check, scoped
        // to this test's own row id, is itself complete proof that some
        // OutboxProcessor instance delivered this exact message via some
        // IEmailClient successfully; nothing further to prove.
        //
        // Deliberately NOT also asserting against MockEmailClient's captured
        // calls here: this table is real, shared MySQL (not per-test
        // isolated), and every [Collection("Integration")] test class gets
        // its own WebApplicationFactory (and thus its own OutboxProcessor
        // background service) - their lifetimes can overlap briefly around
        // class-fixture teardown/startup, so a *different* test class's
        // OutboxProcessor can win the race to deliver *this* row through
        // *its own* separate MockEmailClient instance, one this test never
        // sees. That happened for real in CI (see the commit fixing this
        // comment). The Status/ProcessedAt check above doesn't have that
        // problem since it reads the row directly, regardless of which
        // processor instance handled it.
        Assert.NotNull(delivered);
        Assert.Equal(OutboxMessageStatus.Sent, delivered!.Status);
        Assert.NotNull(delivered.ProcessedAt);
    }
}
