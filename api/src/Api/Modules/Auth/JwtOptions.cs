namespace Api.Modules.Auth;

/// <summary>
/// Binds the "Jwt" configuration section. In Development, SigningKey comes
/// from appsettings.Development.json (git-ignored, dev-only random key —
/// see comment there). Production MUST supply this via dotnet user-secrets
/// or a GitHub Actions encrypted secret per CLAUDE.md's secrets convention;
/// never hardcode a real key in source.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = null!;
    public string Issuer { get; set; } = "CapacityRequestSystem";
    public string Audience { get; set; } = "CapacityRequestSystem";
    public int AccessTokenMinutes { get; set; } = 15;
}
