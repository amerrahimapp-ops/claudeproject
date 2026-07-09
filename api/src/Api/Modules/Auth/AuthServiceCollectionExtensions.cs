using System.Text;
using Api.Data.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Api.Modules.Auth;

/// <summary>
/// Auth module wiring: identity provider selection (config-driven, matching
/// the "Provider": "Mock"|"Ad" pattern used by other integrations), JWT
/// issuance, and JWT bearer authentication middleware registration.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // "Provider" config switch, same pattern as Grafana/Email/Jira (see
        // CLAUDE.md / design spec Section 10). Only "Mock" exists so far —
        // AdIdentityProvider is deferred (see IIdentityProvider doc comment).
        var provider = configuration["Auth:Provider"] ?? "Mock";

        // Fail fast rather than silently accepting any password: MockIdentityProvider
        // must never be reachable outside Development, even if "Auth:Provider" is
        // left unset or misconfigured in a non-dev appsettings file.
        if (provider == "Mock" && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Auth:Provider is \"Mock\" (or unset) in the \"{environment.EnvironmentName}\" environment. " +
                "MockIdentityProvider accepts any password and must only run in Development. " +
                "Set Auth:Provider to a real provider for this environment.");
        }

        switch (provider)
        {
            case "Mock":
            default:
                services.AddSingleton<IIdentityProvider, MockIdentityProvider>();
                break;
        }

        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var signingKey = jwtSection["SigningKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it in appsettings.Development.json for local " +
                "dev, or via dotnet user-secrets / GitHub Actions secrets for other environments.");
        var issuer = jwtSection["Issuer"] ?? "CapacityRequestSystem";
        var audience = jwtSection["Audience"] ?? "CapacityRequestSystem";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();

        return services;
    }
}
