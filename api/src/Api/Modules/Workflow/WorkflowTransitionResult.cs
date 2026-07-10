using Api.Data.Entities;

namespace Api.Modules.Workflow;

/// <summary>
/// Distinguishes *why* a transition failed so the API layer can map to the
/// right HTTP status code (404 / 409 / 403 respectively). Kept as separate
/// buckets rather than a single bool so "nonsensical transition" and
/// "allowed transition, wrong actor" never get conflated.
/// </summary>
public enum WorkflowTransitionOutcome
{
    Success,
    RequestNotFound,
    IllegalTransition,
    WrongRole,
}

public class WorkflowTransitionResult
{
    public required WorkflowTransitionOutcome Outcome { get; init; }
    public string? FailureReason { get; init; }
    public Request? Request { get; init; }

    public static WorkflowTransitionResult NotFound() =>
        new() { Outcome = WorkflowTransitionOutcome.RequestNotFound };

    public static WorkflowTransitionResult Illegal(string reason) =>
        new() { Outcome = WorkflowTransitionOutcome.IllegalTransition, FailureReason = reason };

    public static WorkflowTransitionResult WrongRole(string reason) =>
        new() { Outcome = WorkflowTransitionOutcome.WrongRole, FailureReason = reason };

    public static WorkflowTransitionResult Success(Request request) =>
        new() { Outcome = WorkflowTransitionOutcome.Success, Request = request };
}
