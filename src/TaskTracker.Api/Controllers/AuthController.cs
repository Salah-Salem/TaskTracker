using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Common;

namespace TaskTracker.Api.Controllers;

[Route("api/auth")]
[Authorize]
public class AuthController(IAuthService auth) : ApiControllerBase
{
    /// <summary>US-A1: create an account (role TeamMember).</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var result = await auth.RegisterAsync(request);
        return result.IsSuccess
            ? Created("/api/auth/me", ApiResponse.Ok(result.Data))
            : FromResult(result);
    }

    /// <summary>US-A2: exchange credentials for a JWT.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request) =>
        FromResult(await auth.LoginAsync(request));

    /// <summary>Exchange a refresh token for a new access + refresh pair. The old refresh token is revoked.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshRequest request) =>
        FromResult(await auth.RefreshAsync(request));

    /// <summary>US-A3: end every session — access tokens (security stamp) and refresh tokens.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await auth.LogoutAsync(CurrentUserId);
        return NoContent();
    }

    /// <summary>Current user + roles.</summary>
    [HttpGet("me")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me()
    {
        var user = await auth.GetUserAsync(CurrentUserId);
        return user is null
            ? StatusCode(StatusCodes.Status401Unauthorized, ApiResponse.Fail("Unauthorized"))
            : Ok(ApiResponse.Ok(user));
    }
}
