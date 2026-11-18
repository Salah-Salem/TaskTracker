using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Common;
using TaskTracker.Domain;
using TaskTracker.Domain.Entities;
using TaskTracker.Infrastructure.Persistence;

namespace TaskTracker.Infrastructure.Auth;

public class AuthService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    JwtTokenService tokens,
    TimeProvider clock,
    ILogger<AuthService> log) : IAuthService
{
    public const string EmailTaken = "Email already registered";
    public const string InvalidCredentials = "Invalid email or password";
    public const string AccountDisabled = "Account disabled";
    public const string AccountLocked = "Account temporarily locked. Try again later.";
    public const string InvalidRefreshToken = "Invalid or expired refresh token";

    public async Task<ServiceResult<UserDto>> RegisterAsync(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email) is not null)
            return ServiceResult<UserDto>.BadRequest(EmailTaken);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = req.DisplayName.Trim(),
        };

        var created = await users.CreateAsync(user, req.Password); // Identity hashes (PBKDF2)
        if (!created.Succeeded)
        {
            // Race on the unique index still surfaces as a clean 400, never a 500.
            if (created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
                return ServiceResult<UserDto>.BadRequest(EmailTaken);
            return ServiceResult<UserDto>.BadRequest(created.Errors.Select(e => e.Description).ToArray());
        }

        await users.AddToRoleAsync(user, Roles.User);
        log.LogInformation("Audit: user {UserId} registered with role {Role}", user.Id, Roles.User);

        return ServiceResult<UserDto>.Ok(ToDto(user, [Roles.User]));
    }

    public async Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest req)
    {
        var user = await users.FindByEmailAsync(req.Email.Trim());
        if (user is null)
            return ServiceResult<AuthResponse>.Unauthorized(InvalidCredentials);

        // lockoutOnFailure: 5 consecutive failures -> 15 min lockout (US-A2).
        var check = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            log.LogWarning("Login blocked: user {UserId} is locked out", user.Id);
            return ServiceResult<AuthResponse>.Unauthorized(AccountLocked);
        }
        if (!check.Succeeded)
        {
            log.LogInformation("Login failed for user {UserId}", user.Id);
            return ServiceResult<AuthResponse>.Unauthorized(InvalidCredentials);
        }
        // Checked only after the password, so "disabled" is not revealed to someone guessing.
        if (!user.IsActive)
            return ServiceResult<AuthResponse>.Unauthorized(AccountDisabled);

        log.LogInformation("User {UserId} logged in", user.Id);
        return ServiceResult<AuthResponse>.Ok(await IssueTokensAsync(user, replacing: null));
    }

    public async Task<ServiceResult<AuthResponse>> RefreshAsync(RefreshRequest req)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = TokenHasher.Hash(req.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);

        if (stored is null)
            return ServiceResult<AuthResponse>.Unauthorized(InvalidRefreshToken);

        if (stored.RevokedAt is not null)
        {
            // A rotated-out token was presented again: assume it was stolen and end every session.
            await RevokeAllRefreshTokensAsync(db, stored.UserId, now);
            log.LogWarning("Refresh token reuse detected for user {UserId}; all refresh tokens revoked", stored.UserId);
            return ServiceResult<AuthResponse>.Unauthorized(InvalidRefreshToken);
        }

        if (stored.ExpiresAt <= now)
            return ServiceResult<AuthResponse>.Unauthorized(InvalidRefreshToken);

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
            return ServiceResult<AuthResponse>.Unauthorized(InvalidRefreshToken);
        if (!user.IsActive)
            return ServiceResult<AuthResponse>.Unauthorized(AccountDisabled);

        // New access token carries the user's *current* roles and stamp (so a role change is picked up here).
        return ServiceResult<AuthResponse>.Ok(await IssueTokensAsync(user, replacing: stored));
    }

    public async Task LogoutAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return;
        await users.UpdateSecurityStampAsync(user);                                  // kills access tokens
        await RevokeAllRefreshTokensAsync(db, user.Id, clock.GetUtcNow().UtcDateTime); // kills refresh tokens
        log.LogInformation("User {UserId} logged out (security stamp rotated, refresh tokens revoked)", user.Id);
    }

    public async Task<UserDto?> GetUserAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : ToDto(user, (await users.GetRolesAsync(user)).ToArray());
    }

    public async Task<bool> IsSessionValidAsync(Guid userId, string securityStampClaim)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is { IsActive: true }
               && TokenHasher.Hash(user.SecurityStamp) == securityStampClaim;
    }

    internal static Task<int> RevokeAllRefreshTokensAsync(AppDbContext db, Guid userId, DateTime now) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, RefreshToken? replacing)
    {
        var roles = (await users.GetRolesAsync(user)).ToArray();
        var (access, accessExpires) = tokens.CreateAccessToken(user, roles);
        var (refresh, entity) = tokens.CreateRefreshToken(user.Id);

        db.RefreshTokens.Add(entity);
        if (replacing is not null)
        {
            replacing.RevokedAt = entity.CreatedAt;
            replacing.ReplacedByTokenId = entity.Id;
        }
        await db.SaveChangesAsync();

        return new AuthResponse(access, accessExpires, refresh, entity.ExpiresAt, ToDto(user, roles));
    }

    private static UserDto ToDto(ApplicationUser u, string[] roles) =>
        new(u.Id, u.Email!, u.DisplayName, roles, u.IsActive);
}
