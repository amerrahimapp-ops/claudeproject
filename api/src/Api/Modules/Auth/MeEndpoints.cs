using System.Security.Claims;
using System.Text.Json;
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

/// <summary>
/// Allowed values for UserPreference.Theme. String-backed on the DB side too
/// (see CapacityDbContext's HasConversion&lt;string&gt;() for this column).
/// </summary>
public static class ThemeOptions
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>([Light, Dark], StringComparer.Ordinal);
}

/// <summary>
/// Fixed, small set of per-event-type notification toggles — deliberately
/// not a fully dynamic/keyed schema (see the Phase 8a task scope). All
/// default to <c>true</c> (opted in) so a brand-new preference row, or a PUT
/// that omits this field entirely, never silently opts anyone out. Stored
/// as the JSON blob in UserPreference.NotificationPrefs. Phase 8b (real
/// email notifications) reads these back to decide whether to send.
/// </summary>
public record NotificationPreferences(
    bool RequestStatusChanged = true,
    bool NewAssignedTask = true);

public record UserPreferencesResponse(
    string DefaultView,
    string Theme,
    NotificationPreferences NotificationPrefs);

/// <summary>
/// Theme and NotificationPrefs are both optional so existing callers that
/// only ever sent <c>{ defaultView }</c> keep working unchanged — omitted
/// fields leave the corresponding stored value untouched (or fall back to
/// the Dark/all-true defaults on first-ever PUT for a user).
/// </summary>
public record UpdateUserPreferencesRequest(
    string DefaultView,
    string? Theme = null,
    NotificationPreferences? NotificationPrefs = null);

/// <summary>
/// Minimal "my preferences" endpoints — own record only, any authenticated
/// user (not Admin-gated). Backed by the UserPreference entity/table that
/// already existed from an earlier phase (one row per user, nullable until
/// first PUT). Exposes DefaultView (Phase 6), Theme, and NotificationPrefs
/// (both Phase 8a).
/// </summary>
public static class MeEndpoints
{
    private static readonly JsonSerializerOptions NotificationPrefsJsonOptions =
        new(JsonSerializerDefaults.Web);

    private static NotificationPreferences ParseNotificationPrefs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new NotificationPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<NotificationPreferences>(json, NotificationPrefsJsonOptions)
                   ?? new NotificationPreferences();
        }
        catch (JsonException)
        {
            // Defensive only — every write through this endpoint produces
            // well-formed JSON. Falls back to defaults rather than 500ing on
            // a corrupt/hand-edited row.
            return new NotificationPreferences();
        }
    }

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me/preferences", async (ClaimsPrincipal user, CapacityDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue("user_id")!);

            var preference = await db.UserPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId);

            return Results.Ok(new UserPreferencesResponse(
                preference?.DefaultView ?? DefaultViewOptions.Dashboard,
                (preference?.Theme ?? ThemePreference.Dark).ToString(),
                ParseNotificationPrefs(preference?.NotificationPrefs)));
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

            if (body.Theme is not null && !ThemeOptions.Allowed.Contains(body.Theme))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown theme \"{body.Theme}\". " +
                             $"Allowed values: {string.Join(", ", ThemeOptions.Allowed)}.",
                });
            }

            var userId = int.Parse(user.FindFirstValue("user_id")!);

            var preference = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
            if (preference is null)
            {
                preference = new UserPreference
                {
                    UserId = userId,
                    NotificationPrefs = JsonSerializer.Serialize(
                        body.NotificationPrefs ?? new NotificationPreferences(),
                        NotificationPrefsJsonOptions),
                    DefaultView = body.DefaultView,
                    Theme = body.Theme is not null
                        ? Enum.Parse<ThemePreference>(body.Theme)
                        : ThemePreference.Dark,
                };
                db.UserPreferences.Add(preference);
            }
            else
            {
                preference.DefaultView = body.DefaultView;

                if (body.Theme is not null)
                {
                    preference.Theme = Enum.Parse<ThemePreference>(body.Theme);
                }

                if (body.NotificationPrefs is not null)
                {
                    preference.NotificationPrefs = JsonSerializer.Serialize(
                        body.NotificationPrefs,
                        NotificationPrefsJsonOptions);
                }
            }

            await db.SaveChangesAsync();

            return Results.Ok(new UserPreferencesResponse(
                preference.DefaultView,
                preference.Theme.ToString(),
                ParseNotificationPrefs(preference.NotificationPrefs)));
        })
        .WithName("UpdateMyPreferences")
        .RequireAuthorization();

        return app;
    }
}
