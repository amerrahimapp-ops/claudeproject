using System.Text.Json;
using Api.Data;
using Api.Data.Entities;

namespace Api.Modules.Integrations.Outbox;

/// <summary>
/// Default IOutboxWriter - just persists a Pending row. Never delivers the
/// message itself; that's OutboxProcessor's job (see design spec Section 2.3:
/// API writes to outbox, background worker delivers to external services).
/// </summary>
public class OutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CapacityDbContext _db;

    public OutboxWriter(CapacityDbContext db)
    {
        _db = db;
    }

    public async Task<int> EnqueueEmailAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        var payload = new EmailOutboxPayload
        {
            ToAddress = toAddress,
            Subject = subject,
            Body = body,
        };

        var message = new OutboxMessage
        {
            MessageType = "Email",
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            Status = OutboxMessageStatus.Pending,
            Attempts = 0,
            CreatedAt = DateTime.UtcNow,
        };

        _db.OutboxMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);
        return message.Id;
    }
}
