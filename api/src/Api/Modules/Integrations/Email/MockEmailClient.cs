using System.Collections.Concurrent;

namespace Api.Modules.Integrations.Email;

/// <summary>
/// Dev/test mock - logs instead of actually sending. Used when
/// Email:Provider is "Mock" or unset, so tests/CI never need real
/// Mailtrap credentials.
/// </summary>
public class MockEmailClient : IEmailClient
{
    private readonly ILogger<MockEmailClient> _logger;

    public MockEmailClient(ILogger<MockEmailClient> logger)
    {
        _logger = logger;
    }

    public record SentEmail(string ToAddress, string Subject, string Body);

    /// <summary>
    /// Every call this instance has received, in delivery order. Registered
    /// as a singleton (see IntegrationsServiceCollectionExtensions), so this
    /// persists across calls within a test run - lets tests (e.g.
    /// OutboxTests) assert the background outbox processor actually invoked
    /// the mock for a specific email, without a real Mailtrap dependency.
    /// A plain "last call" isn't reliable here: OutboxProcessor delivers any
    /// Pending row in the shared table, including rows other concurrently-
    /// running tests enqueued, so more than one email can land in a batch.
    /// </summary>
    public ConcurrentQueue<SentEmail> Calls { get; } = new();

    /// <summary>Number of times SendAsync has been called.</summary>
    public int SentCount => Calls.Count;

    public string? LastToAddress => Calls.IsEmpty ? null : Calls.Last().ToAddress;
    public string? LastSubject => Calls.IsEmpty ? null : Calls.Last().Subject;
    public string? LastBody => Calls.IsEmpty ? null : Calls.Last().Body;

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(new SentEmail(toAddress, subject, body));

        _logger.LogInformation(
            "[MockEmailClient] Would send to {ToAddress}, subject {Subject}: {Body}", toAddress, subject, body);
        return Task.CompletedTask;
    }
}
