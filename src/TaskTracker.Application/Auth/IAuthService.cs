using TaskTracker.Application.Common;

namespace TaskTracker.Application.Auth;

public interface IAuthService
{
    Task<ServiceResult<UserDto>> RegisterAsync(RegisterRequest request);
    Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request);

    /// <summary>Exchanges a refresh token for a new pair. The presented token is revoked (rotation).</summary>
    Task<ServiceResult<AuthResponse>> RefreshAsync(RefreshRequest request);

    /// <summary>Rotates the security stamp and revokes every refresh token: all sessions end.</summary>
    Task LogoutAsync(Guid userId);

    Task<UserDto?> GetUserAsync(Guid userId);

    /// <summary>Per-request check behind every JWT: the user exists, is active, and the token's stamp is current.</summary>
    Task<bool> IsSessionValidAsync(Guid userId, string securityStampClaim);
}
