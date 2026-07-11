namespace Api.Modules.Integrations.Outbox;

/// <summary>
/// Binds the "Outbox" config section. PollIntervalSeconds defaults to a
/// short value so integration tests (see OutboxTests) can poll for delivery
/// within a practical timeout without needing to override config.
/// </summary>
public class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 10;
    public int MaxAttempts { get; set; } = 3;
}
