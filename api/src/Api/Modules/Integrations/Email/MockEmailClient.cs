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

    /// <summary>
    /// Number of times SendAsync has been called. Registered as a singleton
    /// (see IntegrationsServiceCollectionExtensions), so this persists across
    /// calls within a test run - lets tests (e.g. OutboxTests) assert the
    /// background outbox processor actually invoked the mock, without a real
    /// Mailtrap dependency.
    /// </summary>
    public int SentCount { get; private set; }

    public string? LastToAddress { get; private set; }
    public string? LastSubject { get; private set; }
    public string? LastBody { get; private set; }

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        SentCount++;
        LastToAddress = toAddress;
        LastSubject = subject;
        LastBody = body;

        _logger.LogInformation(
            "[MockEmailClient] Would send to {ToAddress}, subject {Subject}: {Body}", toAddress, subject, body);
        return Task.CompletedTask;
    }
}
