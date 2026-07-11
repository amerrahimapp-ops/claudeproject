namespace Api.Modules.Integrations.Email;

public record TestEmailRequest(string ToAddress);

/// <summary>
/// Diagnostic endpoint for verifying the configured email provider actually
/// works (real Mailtrap send, or mock log line) - not part of the product
/// feature set, just an ops/dev tool. Admin-only.
/// </summary>
public static class EmailEndpoints
{
    public static IEndpointRouteBuilder MapEmailEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/admin/test-email", async (TestEmailRequest body, IEmailClient emailClient) =>
        {
            await emailClient.SendAsync(
                body.ToAddress,
                "Capacity Request System - test email",
                "This is a test email confirming the configured email provider works.");
            return Results.Ok(new { sent = true });
        })
        .WithName("SendTestEmail")
        .RequireAuthorization(policy => policy.RequireRole("Admin"));

        return app;
    }
}
