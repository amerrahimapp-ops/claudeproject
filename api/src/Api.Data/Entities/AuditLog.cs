namespace Api.Data.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public string EntityType { get; set; } = null!;
    public int EntityId { get; set; }
    public string Action { get; set; } = null!;

    /// <summary>JSON snapshot of the entity's field values before the change.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON snapshot of the entity's field values after the change.</summary>
    public string? NewValues { get; set; }

    public int PerformedByUserId { get; set; }
    public User PerformedByUser { get; set; } = null!;
    public DateTime PerformedAt { get; set; }
}
