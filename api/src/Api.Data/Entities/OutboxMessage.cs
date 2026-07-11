namespace Api.Data.Entities;

/// <summary>
/// Async outbox row (design spec Section 2.3 "Integration Bus"). API writes
/// rows here synchronously with the triggering request; a background worker
/// (see Api.Modules.Integrations.Outbox) delivers them to external services
/// (email now, Jira/HPSM later) out of band, so a downstream outage never
/// blocks the API and every delivery attempt is auditable.
/// </summary>
public class OutboxMessage
{
    public int Id { get; set; }

    /// <summary>Discriminates how Payload should be interpreted, e.g. "Email".</summary>
    public string MessageType { get; set; } = null!;

    /// <summary>JSON blob; shape depends on MessageType (e.g. toAddress/subject/body for "Email").</summary>
    public string Payload { get; set; } = null!;

    public OutboxMessageStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public enum OutboxMessageStatus
{
    Pending,
    Processing,
    Sent,
    Failed,
}
