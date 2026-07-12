using System.Globalization;
using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Api.Modules.Auth;
using Api.Modules.Integrations.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Notifications;

/// <summary>
/// Thin notification abstraction sitting on top of <see cref="IOutboxWriter"/>
/// (design spec 2.3/4.4/10.5): WorkflowEngine/WorkflowAutomationService call
/// these two methods instead of constructing outbox email payloads inline,
/// and every per-user notificationPrefs opt-out check lives in exactly one
/// place. This is the "real content" this module gained in Phase 8b - see
/// docs/progress/phase-8b-status.md for why the module wasn't just deleted
/// instead.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Notifies the request's owner that their request's status changed,
    /// unless they've opted out via notificationPrefs.requestStatusChanged.
    /// </summary>
    Task NotifyRequestStatusChangedAsync(
        Request request, RequestStatus oldStatus, RequestStatus newStatus, string? comments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies every user with <paramref name="requiredRole"/> that a new
    /// task is waiting for them at <paramref name="stageName"/>, unless they
    /// individually opted out via notificationPrefs.newAssignedTask. There's
    /// no single "assigned user" per stage in this app - roles, not
    /// individuals, own queues - so every user with the role is a valid
    /// recipient.
    /// </summary>
    Task NotifyRoleOfNewTaskAsync(
        Request request, string stageName, string requiredRole, string? comments,
        CancellationToken cancellationToken = default);
}

public class NotificationService : INotificationService
{
    // Mirrors MeEndpoints.ParseNotificationPrefs exactly (same JSON shape,
    // same fallback-to-defaults-on-corrupt-data behavior). Duplicated rather
    // than calling into Api.Modules.Auth's endpoint-mapping class from here -
    // that class isn't a service, and NotificationPreferences deserialization
    // is a couple of lines, not worth an extra cross-module interface for.
    private static readonly JsonSerializerOptions NotificationPrefsJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOutboxWriter _outboxWriter;
    private readonly CapacityDbContext _db;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(IOutboxWriter outboxWriter, CapacityDbContext db, ILogger<NotificationService> logger)
    {
        _outboxWriter = outboxWriter;
        _db = db;
        _logger = logger;
    }

    public async Task NotifyRequestStatusChangedAsync(
        Request request, RequestStatus oldStatus, RequestStatus newStatus, string? comments,
        CancellationToken cancellationToken = default)
    {
        var prefs = await GetNotificationPrefsAsync(request.RequestorUserId, cancellationToken);
        if (!prefs.RequestStatusChanged)
        {
            _logger.LogInformation(
                "Skipped status-changed notification for request {RequestNumber}: owner (user {UserId}) has requestStatusChanged disabled.",
                request.RequestNumber, request.RequestorUserId);
            return;
        }

        var subject = $"Capacity Request {request.RequestNumber}: status changed to {Humanize(newStatus)}";
        var body =
            $"Your capacity request {request.RequestNumber} ({request.Title}) changed status: " +
            $"{Humanize(oldStatus)} -> {Humanize(newStatus)}." +
            (string.IsNullOrWhiteSpace(comments) ? string.Empty : $"\n\nComment: {comments}");

        await _outboxWriter.EnqueueEmailAsync(request.RequestorUser.Email, subject, body, cancellationToken);
    }

    public async Task NotifyRoleOfNewTaskAsync(
        Request request, string stageName, string requiredRole, string? comments,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<UserRole>(requiredRole, out var role))
        {
            _logger.LogWarning(
                "Unrecognized RequiredRole \"{RequiredRole}\" on stage \"{StageName}\"; skipping new-task notification.",
                requiredRole, stageName);
            return;
        }

        var recipients = await _db.Users.AsNoTracking()
            .Where(u => u.Role == role)
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "No users with role {Role} found to notify about request {RequestNumber} reaching {StageName}.",
                role, request.RequestNumber, stageName);
            return;
        }

        var stageLabel = Humanize(stageName);
        var subject = $"Capacity Request {request.RequestNumber}: new task waiting in {stageLabel}";
        var body =
            $"Capacity request {request.RequestNumber} ({request.Title}) is now waiting for {role} review " +
            $"at the {stageLabel} stage." +
            (string.IsNullOrWhiteSpace(comments) ? string.Empty : $"\n\nComment: {comments}");

        foreach (var recipient in recipients)
        {
            var prefs = await GetNotificationPrefsAsync(recipient.Id, cancellationToken);
            if (!prefs.NewAssignedTask)
            {
                _logger.LogInformation(
                    "Skipped new-task notification to user {UserId}: newAssignedTask disabled.", recipient.Id);
                continue;
            }

            await _outboxWriter.EnqueueEmailAsync(recipient.Email, subject, body, cancellationToken);
        }
    }

    private async Task<NotificationPreferences> GetNotificationPrefsAsync(int userId, CancellationToken cancellationToken)
    {
        var json = await _db.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.NotificationPrefs)
            .FirstOrDefaultAsync(cancellationToken);

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
            // Defensive only, same reasoning as MeEndpoints.ParseNotificationPrefs:
            // fall back to opted-in defaults rather than losing a notification
            // (or throwing) over a corrupt/hand-edited row.
            return new NotificationPreferences();
        }
    }

    private static string Humanize(RequestStatus status) => Humanize(status.ToStageName());

    private static string Humanize(string snakeCaseStageName) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(snakeCaseStageName.Replace('_', ' '));
}
