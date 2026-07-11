namespace Api.Data.Entities;

/// <summary>
/// Full audit trail of every AI evaluation call - prompt and response are
/// stored verbatim (not just the parsed score/recommendation) since this
/// is an approval-affecting decision that must be traceable (ADR 0002).
/// </summary>
public class AiEvaluation
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public string Prompt { get; set; } = null!;
    public string RawResponse { get; set; } = null!;

    /// <summary>Null if the response failed to parse into the expected shape.</summary>
    public double? Score { get; set; }
    public string? Recommendation { get; set; }

    /// <summary>JSON array of flag strings, null if parsing failed.</summary>
    public string? FlagsJson { get; set; }

    public DateTime EvaluatedAt { get; set; }
}
