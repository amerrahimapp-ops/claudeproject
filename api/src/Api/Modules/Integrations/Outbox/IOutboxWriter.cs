namespace Api.Modules.Integrations.Outbox;

/// <summary>
/// Writes messages to the outbox table (design spec Section 2.3). Callers
/// enqueue here synchronously with their own unit of work; delivery to the
/// external service happens later, out of band, in OutboxProcessor.
/// </summary>
public interface IOutboxWriter
{
    /// <summary>Enqueues an email and returns the new OutboxMessage's id — lets a caller
    /// (e.g. a test) unambiguously find its own row later instead of guessing via
    /// ordering, which is unreliable once more than one write can land in the table
    /// around the same time.</summary>
    Task<int> EnqueueEmailAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default);
}
