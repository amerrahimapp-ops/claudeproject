using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Api.Modules.Integrations.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Integrations.Outbox;

/// <summary>
/// Background worker half of the async outbox pattern (design spec Section
/// 2.3): polls for Pending OutboxMessage rows and delivers them to the
/// relevant external service, so the API never blocks on a downstream
/// integration (e.g. Mailtrap/Jira) being slow or unavailable.
///
/// BackgroundService itself is registered as a singleton, but CapacityDbContext
/// is scoped - so a new DI scope is created per tick to resolve a fresh
/// DbContext (and IEmailClient) rather than holding one for the service's
/// entire lifetime.
///
/// Retry policy: a Failed message is reset to Pending and retried as long as
/// Attempts &lt; MaxAttempts; once that ceiling is hit it's left Failed
/// permanently. No exponential backoff/dead-letter queue - deliberately
/// simple for Phase 1 scale (see project's proportionality principle).
/// </summary>
public class OutboxProcessor : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, IOptions<OutboxOptions> options, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));

        do
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox processor tick failed unexpectedly.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapacityDbContext>();
        var emailClient = scope.ServiceProvider.GetRequiredService<IEmailClient>();

        // Give previously-failed-but-retryable messages another chance before
        // picking up new Pending work.
        var retryable = await db.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.Failed && m.Attempts < _options.MaxAttempts)
            .ToListAsync(cancellationToken);

        if (retryable.Count > 0)
        {
            foreach (var message in retryable)
            {
                message.Status = OutboxMessageStatus.Pending;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var batch = await db.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var message in batch)
        {
            message.Status = OutboxMessageStatus.Processing;
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var message in batch)
        {
            await DeliverAsync(db, emailClient, message, cancellationToken);
        }
    }

    private async Task DeliverAsync(CapacityDbContext db, IEmailClient emailClient, OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            switch (message.MessageType)
            {
                case "Email":
                    var payload = JsonSerializer.Deserialize<EmailOutboxPayload>(message.Payload, JsonOptions)
                        ?? throw new InvalidOperationException("Email outbox payload deserialized to null.");
                    await emailClient.SendAsync(payload.ToAddress, payload.Subject, payload.Body, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown outbox message type '{message.MessageType}'.");
            }

            message.Status = OutboxMessageStatus.Sent;
            message.ProcessedAt = DateTime.UtcNow;
            _logger.LogInformation(
                "Outbox message {Id} ({MessageType}) delivered successfully.", message.Id, message.MessageType);
        }
        catch (Exception ex)
        {
            message.Attempts++;
            message.Status = OutboxMessageStatus.Failed;
            message.LastError = ex.Message;
            _logger.LogWarning(
                ex, "Outbox message {Id} ({MessageType}) delivery failed on attempt {Attempts}.",
                message.Id, message.MessageType, message.Attempts);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
