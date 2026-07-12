using System.Text;
using System.Threading.RateLimiting;
using Api.Data.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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

        // HS256 (see JwtTokenService) is only as strong as its key: RFC 7518
        // requires a key >= the hash output size (256 bits / 32 bytes) for
        // HMAC-SHA256. Fail fast rather than silently issuing tokens signed
        // with a weak key that could be brute-forced offline.
        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 bytes (256 bits) for HS256 - the configured key is " +
                "too short and would weaken token security. Use a longer randomly-generated key.");
        }

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

        // Brute-force protection on /api/v1/auth/login: MockIdentityProvider
        // accepts any non-empty password for a known username (Development
        // only - see the fail-fast check above), and a real AdIdentityProvider
        // will eventually sit behind this same endpoint, so unlimited login
        // attempts would let an attacker script credential guesses with no
        // friction. A single-instance, in-memory fixed-window limiter is
        // proportional here (this app has no distributed/multi-instance
        // deployment yet - see docs/runbook.md); it resets on restart, which
        // is acceptable for this threat model. Partitioned by client IP so
        // one noisy caller doesn't lock out everyone else.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}
