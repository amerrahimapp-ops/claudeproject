namespace Api.Modules.Notifications;

/// <summary>
/// Notifications module wiring. Empty scaffolding for the Foundation
/// phase — email/notification dispatch lands in a later phase.
/// </summary>
public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        return services;
    }
}
