using Api.Data.Auth;
using Microsoft.Extensions.Options;

namespace Api.Modules.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/login", async (
            LoginRequest request,
            IIdentityProvider identityProvider,
            IJwtTokenService jwtTokenService,
            IOptions<JwtOptions> jwtOptions) =>
        {
            var authResult = await identityProvider.AuthenticateAsync(request.Username, request.Password);
            if (authResult is null)
            {
                return Results.Unauthorized();
            }

            var token = jwtTokenService.IssueToken(authResult);
            var response = new LoginResponse(
                token,
                jwtOptions.Value.AccessTokenMinutes,
                authResult.DisplayName,
                authResult.Role.ToString());

            return Results.Ok(response);
        })
        .WithName("Login")
        .RequireRateLimiting("login");

        return app;
    }
}
