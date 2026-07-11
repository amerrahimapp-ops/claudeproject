namespace Api.Modules.Reports;

/// <summary>
/// Reports module wiring (Excel export via ClosedXML per ADR 0001).
/// One real implementation for now — no Mock/Provider switch, since report
/// generation has no external dependency to stub out.
/// </summary>
public static class ReportsServiceCollectionExtensions
{
    public static IServiceCollection AddReportsModule(this IServiceCollection services)
    {
        services.AddSingleton<IReportGenerator, ClosedXmlReportGenerator>();
        return services;
    }
}
