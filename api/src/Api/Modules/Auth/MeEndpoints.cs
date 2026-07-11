using System.Security.Claims;
using Api.Data;
using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Auth;

/// <summary>
/// Allowed values for UserPreference.DefaultView. Deliberately just three
/// options for Phase 6 polish (Dashboard / New Request / the caller's own
/// approval queue) — not a general settings system. "ApprovalQueue" is
/// resolved to the right queue route on the frontend based on the user's
/// role (CapacityManager -> capacity-review, InfraHead -> infra-approval);
/// for a Requestor/Admin it just falls back to Dashboard there, so the
/// backend doesn't need to know about roles at all here.
/// </summary>
public static class DefaultViewOptions
{
    public const string Dashboard = "Dashboard";
    public const string NewRequest = "NewRequest";
    public const string ApprovalQueue = "ApprovalQueue";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>([Dashboard, NewRequest, ApprovalQueue], StringComparer.Ordinal);
}

public record UserPreferencesResponse(string DefaultView);

public record UpdateUserPreferencesRequest(string DefaultView);

/// <summary>
/// Minimal "my preferences" endpoints — own record only, any authenticated
/// user (not Admin-gated). Backed by the UserPreference entity/table that
/// already existed from an earlier phase (one row per user, nullable until
/// first PUT). Only DefaultView is exposed here; NotificationPrefs/Theme on
/// the same row are out of scope for this Phase 6 task.
/// </summary>
public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me/preferences", async (ClaimsPrincipal user, CapacityDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue("user_id")!);

            var preference = await db.UserPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId);

            return Results.Ok(new UserPreferencesResponse(preference?.DefaultView ?? DefaultViewOptions.Dashboard));
        })
        .WithName("GetMyPreferences")
        .RequireAuthorization();

        app.MapPut("/api/v1/me/preferences", async (
            UpdateUserPreferencesRequest body, ClaimsPrincipal user, CapacityDbContext db) =>
        {
            if (!DefaultViewOptions.Allowed.Contains(body.DefaultView))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown defaultView \"{body.DefaultView}\". " +
                             $"Allowed values: {string.Join(", ", DefaultViewOptions.Allowed)}.",
                });
            }

            var userId = int.Parse(user.FindFirstValue("user_id")!);

            var preference = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
            if (preference is null)
            {
                preference = new UserPreference
                {
                    UserId = userId,
                    NotificationPrefs = "{}",
                    DefaultView = body.DefaultView,
                    Theme = ThemePreference.Dark,
                };
                db.UserPreferences.Add(preference);
            }
            else
            {
                preference.DefaultView = body.DefaultView;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new UserPreferencesResponse(preference.DefaultView));
        })
        .WithName("UpdateMyPreferences")
        .RequireAuthorization();

        return app;
    }
}
