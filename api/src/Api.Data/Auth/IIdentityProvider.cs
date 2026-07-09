namespace Api.Data.Auth;

/// <summary>
/// Abstraction over "who is this user and what role do they have".
/// <see cref="MockIdentityProvider"/> is the local-dev implementation.
/// A real <c>AdIdentityProvider</c> (backed by Active Directory) is
/// deferred to Phase 2+/later, pending the real AD endpoint and group
/// names from the business (see CLAUDE.md "Pending business decisions").
/// </summary>
public interface IIdentityProvider
{
    Task<AuthResult?> AuthenticateAsync(string username, string password);
}
