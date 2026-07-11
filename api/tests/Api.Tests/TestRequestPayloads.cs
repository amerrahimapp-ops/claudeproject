namespace Api.Tests;

/// <summary>
/// Shared valid POST /api/v1/requests body for tests that just need *some*
/// draft request to exist and don't care about its specific field values
/// (workflow transitions, report generation, AI evaluation, etc.). Centralized
/// here so the full Phase 7a wizard payload shape (title/department/
/// projectName/projectCode/sponsor/dates/resources — see
/// api/src/Api/Modules/Requests/RequestsDtos.cs) only needs to be kept in
/// sync with the API contract in one place instead of once per test file.
/// </summary>
internal static class TestRequestPayloads
{
    public static object ValidCreateRequest(
        string environment = "Prod",
        string projectType = "New",
        string priority = "Medium") => new
        {
            title = "Test Request",
            department = "IT",
            projectName = "Test Project",
            projectCode = "TP-001",
            sponsor = "Jane Sponsor",
            environment,
            projectType,
            priority,
            startDate = "2026-08-01T00:00:00Z",
            endDate = "2026-12-31T00:00:00Z",
            resources = new[]
            {
                new { resourceType = "Storage", currentValue = 100, requestedValue = 150 },
            },
        };
}
