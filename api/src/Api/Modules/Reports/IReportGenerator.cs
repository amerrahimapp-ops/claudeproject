using Api.Data.Entities;

namespace Api.Modules.Reports;

/// <summary>
/// Generates the Phase-1 Excel report for a single request (spec Section 9,
/// ADR 0001). Same interface + implementation pattern as the Integrations
/// module's I&lt;Thing&gt;Client (see IGrafanaClient/IEmailClient) — there's
/// only one real implementation for now (no Mock), since this has no
/// external dependency to stub out in tests.
/// </summary>
public interface IReportGenerator
{
    /// <summary>
    /// Builds the workbook for <paramref name="request"/> and returns it as
    /// an in-memory XLSX byte array. Callers must eager-load
    /// <c>WorkflowStages</c> (and, for the Requestor's display name,
    /// <c>RequestorUser</c>) before calling this — no lazy-loading proxies
    /// are configured on <c>CapacityDbContext</c>.
    /// </summary>
    byte[] GenerateRequestReport(Request request);
}
