using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

/// <summary>
/// Seeds baseline reference data for local development: the Phase-1
/// workflow_config rows and one local dev admin user. Run in Development
/// at startup (see Program.cs).
/// </summary>
public static class DbInitializer
{
    public static async Task SeedAsync(CapacityDbContext db)
    {
        await SeedDevUsersAsync(db);
        await SeedWorkflowConfigAsync(db);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds one Users row per MockIdentityProvider dev account (see
    /// MockIdentityProvider.cs) so that the "user_id" JWT claim issued on
    /// login for any of them resolves to a real Users row — Request.RequestorUserId
    /// and AuditLog.PerformedByUserId are both FK-constrained, so logging in as
    /// e.g. "requestor.dev" and creating/transitioning a Request would otherwise
    /// fail with a foreign-key violation. Each user is seeded independently
    /// (rather than bailing out early once "admin" exists) so this stays
    /// idempotent even against a dev DB that only has the original "admin" row.
    ///
    /// Ids are assigned explicitly to match MockIdentityProvider's hardcoded
    /// AuthResult.UserId values 1-4 exactly — relying on AUTO_INCREMENT order
    /// alone is fragile (a rolled-back insert, e.g. from a transient deadlock
    /// during a concurrent test run, silently burns a sequence value and
    /// desyncs the two).
    /// </summary>
    private static async Task SeedDevUsersAsync(CapacityDbContext db)
    {
        var devUsers = new[]
        {
            new User { Id = 1, AdUsername = "admin", DisplayName = "Local Admin", Role = UserRole.Admin, Email = "admin@dev.local" },
            new User { Id = 2, AdUsername = "requestor.dev", DisplayName = "Dev Requestor", Role = UserRole.Requestor, Email = "requestor.dev@dev.local" },
            new User { Id = 3, AdUsername = "capacitymanager.dev", DisplayName = "Dev Capacity Manager", Role = UserRole.CapacityManager, Email = "capacitymanager.dev@dev.local" },
            new User { Id = 4, AdUsername = "infrahead.dev", DisplayName = "Dev Infra Head", Role = UserRole.InfraHead, Email = "infrahead.dev@dev.local" },
        };

        foreach (var user in devUsers)
        {
            var exists = await db.Users.AnyAsync(u => u.AdUsername == user.AdUsername);
            if (exists)
            {
                continue;
            }

            user.CreatedAt = DateTime.UtcNow;
            db.Users.Add(user);
        }
    }

    private static async Task SeedWorkflowConfigAsync(CapacityDbContext db)
    {
        var exists = await db.WorkflowConfigs.AnyAsync();
        if (exists)
        {
            return;
        }

        // Phase-1 flow only: draft -> submitted -> ai_evaluation ->
        // ai_reviewed -> capacity_review -> infra_approval -> done.
        // Stages 8-9 of the full 9-stage flow are Phase 2+ (see CLAUDE.md).
        var rows = new[]
        {
            new WorkflowConfig
            {
                StageName = "draft",
                SequenceOrder = 1,
                AllowedTransitions = """["submitted"]""",
                RequiredRole = null,
            },
            new WorkflowConfig
            {
                StageName = "submitted",
                SequenceOrder = 2,
                AllowedTransitions = """["ai_evaluation"]""",
                RequiredRole = nameof(UserRole.Requestor),
            },
            new WorkflowConfig
            {
                StageName = "ai_evaluation",
                SequenceOrder = 3,
                AllowedTransitions = """["ai_reviewed"]""",
                RequiredRole = null,
            },
            new WorkflowConfig
            {
                StageName = "ai_reviewed",
                SequenceOrder = 4,
                AllowedTransitions = """["submitted", "capacity_review"]""",
                RequiredRole = null,
            },
            new WorkflowConfig
            {
                StageName = "capacity_review",
                SequenceOrder = 5,
                AllowedTransitions = """["infra_approval", "rejected", "deferred"]""",
                RequiredRole = nameof(UserRole.CapacityManager),
            },
            new WorkflowConfig
            {
                StageName = "infra_approval",
                SequenceOrder = 6,
                AllowedTransitions = """["done", "rejected", "deferred"]""",
                RequiredRole = nameof(UserRole.InfraHead),
            },
            new WorkflowConfig
            {
                StageName = "done",
                SequenceOrder = 7,
                AllowedTransitions = "[]",
                RequiredRole = null,
            },
        };

        db.WorkflowConfigs.AddRange(rows);
    }
}
