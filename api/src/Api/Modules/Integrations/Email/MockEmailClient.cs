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

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[MockEmailClient] Would send to {ToAddress}, subject {Subject}: {Body}", toAddress, subject, body);
        return Task.CompletedTask;
    }
}
