namespace Api.Modules.Workflow;

/// <summary>
/// Workflow engine module wiring. Empty scaffolding for the Foundation
/// phase — the state machine itself lands in a later phase (and per
/// CLAUDE.md, stays on Claude Code rather than being delegated).
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowModule(this IServiceCollection services)
    {
        return services;
    }
}
