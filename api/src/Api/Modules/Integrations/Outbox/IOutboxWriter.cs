namespace Api.Modules.Integrations.Outbox;

/// <summary>
/// Writes messages to the outbox table (design spec Section 2.3). Callers
/// enqueue here synchronously with their own unit of work; delivery to the
/// external service happens later, out of band, in OutboxProcessor.
/// </summary>
public interface IOutboxWriter
{
    Task EnqueueEmailAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default);
}
