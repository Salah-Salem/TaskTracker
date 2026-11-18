namespace TaskTracker.Application.Auth;

public record RegisterRequest(string Email, string Password, string DisplayName);

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

/// <param name="AccessToken">JWT, 15 min.</param>
/// <param name="RefreshToken">Opaque, single-use, 7 days. Exchange at POST /api/auth/refresh.</param>
public record AuthResponse(
    string AccessToken, DateTime ExpiresAtUtc,
    string RefreshToken, DateTime RefreshTokenExpiresAtUtc,
    UserDto User);

public record UserDto(Guid Id, string Email, string DisplayName, string[] Roles, bool IsActive);
