namespace Api.Modules.Requests;

/// <summary>
/// Requests module wiring. Empty scaffolding for the Foundation phase —
/// business logic (CRUD, request-number generation, etc.) lands in a
/// later phase.
/// </summary>
public static class RequestsServiceCollectionExtensions
{
    public static IServiceCollection AddRequestsModule(this IServiceCollection services)
    {
        return services;
    }
}
