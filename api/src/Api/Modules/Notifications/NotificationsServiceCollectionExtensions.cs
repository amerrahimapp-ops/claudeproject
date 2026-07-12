namespace Api.Modules.Notifications;

/// <summary>
/// Notifications module wiring. Registers <see cref="INotificationService"/>,
/// the abstraction WorkflowEngine/WorkflowAutomationService call into to
/// notify a request's owner (status changed) and a stage's required role
/// (new task waiting) - see docs/progress/phase-8b-status.md. Was empty
/// scaffolding through Phase 7; this is the first real content it's had.
/// </summary>
public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationService, NotificationService>();
        return services;
    }
}
