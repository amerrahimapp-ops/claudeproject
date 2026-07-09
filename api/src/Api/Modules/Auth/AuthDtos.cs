namespace Api.Modules.Auth;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string AccessToken, int ExpiresInMinutes, string DisplayName, string Role);
