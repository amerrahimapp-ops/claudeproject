namespace Api.Modules.Integrations.Grafana;

/// <summary>
/// Diagnostic endpoint for verifying the configured Grafana provider
/// actually works - not part of the product feature set, just an ops/dev
/// tool. Admin-only. Defaults to a trivial "up" query so it works against
/// any Prometheus datasource regardless of what's actually being scraped.
/// </summary>
public static class GrafanaEndpoints
{
    public static IEndpointRouteBuilder MapGrafanaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/test-grafana", async (IGrafanaClient grafanaClient, string? query) =>
        {
            var promQl = query ?? "up";
            var result = await grafanaClient.QueryRangeAsync(
                promQl, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, "15s");

            return result.Success
                ? Results.Ok(new { success = true, data = result.RawResponse!.RootElement })
                : Results.Json(new { success = false, error = result.ErrorMessage }, statusCode: 502);
        })
        .WithName("TestGrafanaQuery")
        .RequireAuthorization(policy => policy.RequireRole("Admin"));

        return app;
    }
}
