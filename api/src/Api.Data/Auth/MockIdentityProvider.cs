using Api.Data.Entities;

namespace Api.Data.Auth;

/// <summary>
/// Local-dev-only identity provider. Hardcodes one user per role; any
/// non-empty password is accepted. This is NEVER used in production —
/// swap the "Provider" config value to a real provider once one exists.
///
/// TODO(Phase 2+/deferred): implement AdIdentityProvider backed by real
/// Active Directory, once the real AD endpoint and group-to-role mapping
/// are provided by the business (see CLAUDE.md "Pending business
/// decisions" — this is explicitly out of scope for Foundation).
/// </summary>
public class MockIdentityProvider : IIdentityProvider
{
    private static readonly IReadOnlyList<AuthResult> DevUsers = new List<AuthResult>
    {
        new()
        {
            UserId = 1,
            AdUsername = "admin",
            DisplayName = "Local Admin",
            Role = UserRole.Admin,
            Email = "admin@dev.local",
        },
        new()
        {
            UserId = 2,
            AdUsername = "requestor.dev",
            DisplayName = "Dev Requestor",
            Role = UserRole.Requestor,
            Email = "requestor.dev@dev.local",
        },
        new()
        {
            UserId = 3,
            AdUsername = "capacitymanager.dev",
            DisplayName = "Dev Capacity Manager",
            Role = UserRole.CapacityManager,
            Email = "capacitymanager.dev@dev.local",
        },
        new()
        {
            UserId = 4,
            AdUsername = "infrahead.dev",
            DisplayName = "Dev Infra Head",
            Role = UserRole.InfraHead,
            Email = "infrahead.dev@dev.local",
        },
    };

    public Task<AuthResult?> AuthenticateAsync(string username, string password)
    {
        // Dev-only: any non-empty password is accepted for a known username.
        if (string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult<AuthResult?>(null);
        }

        var match = DevUsers.FirstOrDefault(u =>
            string.Equals(u.AdUsername, username, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }
}
