using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TaskTracker.Domain.Entities;

namespace TaskTracker.Infrastructure.Auth;

public static class AuthClaimTypes
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string SecurityStamp = "security_stamp";
}

/// <summary>SHA-256 → base64. Used for the security-stamp claim and for storing refresh tokens.</summary>
public static class TokenHasher
{
    public static string Hash(string? value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
}

public class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _opt = options.Value;

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_opt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AuthClaimTypes.Subject, user.Id.ToString()),
            new(AuthClaimTypes.Email, user.Email ?? string.Empty),
            new(AuthClaimTypes.Name, user.DisplayName),
            // Hash, never the raw stamp: the stamp itself must not leave the server (Section 14).
            new(AuthClaimTypes.SecurityStamp, TokenHasher.Hash(user.SecurityStamp)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(AuthClaimTypes.Role, r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _opt.Issuer,
            Audience = _opt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key)), SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    /// <summary>Random 64-byte token. Returns the raw value (for the client) and the entity (stores only the hash).</summary>
    public (string Raw, RefreshToken Entity) CreateRefreshToken(Guid userId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return (raw, new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = TokenHasher.Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_opt.RefreshTokenDays),
        });
    }
}
