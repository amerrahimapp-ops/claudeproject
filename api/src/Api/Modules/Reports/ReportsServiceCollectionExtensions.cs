namespace Api.Modules.Reports;

/// <summary>
/// Reports module wiring (Excel export via ClosedXML per ADR 0001).
/// Empty scaffolding for the Foundation phase — report generation lands
/// in a later phase.
/// </summary>
public static class ReportsServiceCollectionExtensions
{
    public static IServiceCollection AddReportsModule(this IServiceCollection services)
    {
        return services;
    }
}
