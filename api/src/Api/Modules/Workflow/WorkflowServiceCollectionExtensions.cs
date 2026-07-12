namespace Api.Modules.Workflow;

/// <summary>
/// Workflow engine module wiring: the Phase 3 state machine
/// (<see cref="IWorkflowEngine"/>) that drives Request.Status transitions
/// against the seeded WorkflowConfig graph.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowModule(this IServiceCollection services)
    {
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IWorkflowAutomationService, WorkflowAutomationService>();
        return services;
    }
}
