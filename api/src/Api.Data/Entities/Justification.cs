namespace Api.Data.Entities;

/// <summary>
/// Generic Q&amp;A pair captured for a request/resource-type combination.
/// No fixed schema per question — question_key is a free-form string key.
/// </summary>
public class Justification
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public ResourceType ResourceType { get; set; }
    public string QuestionKey { get; set; } = null!;
    public string AnswerText { get; set; } = null!;
}
