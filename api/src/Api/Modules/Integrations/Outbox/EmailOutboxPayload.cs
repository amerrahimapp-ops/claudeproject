namespace Api.Modules.Integrations.Outbox;

/// <summary>Payload shape stored in OutboxMessage.Payload when MessageType == "Email".</summary>
public class EmailOutboxPayload
{
    public string ToAddress { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string Body { get; set; } = null!;
}
