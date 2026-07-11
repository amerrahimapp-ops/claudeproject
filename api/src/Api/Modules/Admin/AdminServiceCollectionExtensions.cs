namespace Api.Modules.Admin;

/// <summary>
/// Admin module wiring (workflow_config management, user administration,
/// audit log viewer). The audit log viewer (see AuditLogEndpoints.cs) landed
/// in Phase 6 — it only reads CapacityDbContext, which is already registered
/// globally in Program.cs, so there's nothing module-specific to register
/// here yet. workflow_config management and user administration are still
/// deferred to a later phase.
/// </summary>
public static class AdminServiceCollectionExtensions
{
    public static IServiceCollection AddAdminModule(this IServiceCollection services)
    {
        return services;
    }
}
