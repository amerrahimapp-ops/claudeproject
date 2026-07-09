using Api.Data.Entities;

namespace Api.Data.Auth;

/// <summary>Result of a successful authentication against an <see cref="IIdentityProvider"/>.</summary>
public class AuthResult
{
    public required int UserId { get; init; }
    public required string AdUsername { get; init; }
    public required string DisplayName { get; init; }
    public required UserRole Role { get; init; }
    public required string Email { get; init; }
}
