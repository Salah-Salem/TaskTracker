using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Auth;
using TaskTracker.Domain.Entities;
using TaskTracker.Infrastructure.Auth;

namespace TaskTracker.Api.Tests.Auth;

public class US_A4_ProtectedRoutesTests(TaskTrackerWebAppFactory factory) : IClassFixture<TaskTrackerWebAppFactory>
{
    [Fact]
    public async Task No_Token_Returns_401_Envelope()
    {
        var res = await factory.CreateClient().GetAsync("/api/auth/me");

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await res.ReadEnvelopeAsync<object>();
        body.Success.Should().BeFalse();
        body.Errors.Should().Equal("Unauthorized");
    }

    [Fact]
    public async Task Garbage_Token_Returns_401()
    {
        var res = await factory.CreateClient().WithBearer("not.a.jwt").GetAsync("/api/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_Signed_With_Another_Key_Returns_401()
    {
        var (email, _) = await factory.CreateClient().RegisterAndLoginAsync();
        var token = await ForgeTokenAsync(email, DateTime.UtcNow.AddMinutes(30), key: new string('x', 64));

        var res = await factory.CreateClient().WithBearer(token).GetAsync("/api/auth/me");

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_Token_Returns_401()
    {
        var (email, _) = await factory.CreateClient().RegisterAndLoginAsync();
        var token = await ForgeTokenAsync(email, DateTime.UtcNow.AddMinutes(-5));

        var res = await factory.CreateClient().WithBearer(token).GetAsync("/api/auth/me");

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Valid_Token_Returns_Current_User_Without_Secrets()
    {
        var (email, auth) = await factory.CreateClient().RegisterAndLoginAsync();

        var res = await factory.CreateClient().WithBearer(auth.AccessToken).GetAsync("/api/auth/me");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await res.Content.ReadAsStringAsync();
        raw.Should().NotContainEquivalentOf("password")
            .And.NotContainEquivalentOf("securityStamp")
            .And.NotContain(auth.AccessToken);
        (await res.ReadEnvelopeAsync<UserDto>()).Data!.Email.Should().Be(email);
    }

    [Fact]
    public async Task Token_Never_Contains_Raw_Security_Stamp()
    {
        var (email, auth) = await factory.CreateClient().RegisterAndLoginAsync();

        var stamp = await GetUserAsync(email, u => u.SecurityStamp!);
        var payload = new JsonWebToken(auth.AccessToken);

        payload.GetClaim(AuthClaimTypes.SecurityStamp).Value.Should().NotBe(stamp);
        payload.EncodedPayload.Should().NotContain(stamp);
    }

    /// <summary>Builds a token for a real user (correct sub + stamp) so only expiry/signature is under test.</summary>
    private async Task<string> ForgeTokenAsync(string email, DateTime expires, string? key = null)
    {
        var opt = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var (id, stamp) = await GetUserAsync(email, u => (u.Id, u.SecurityStamp));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = opt.Issuer,
            Audience = opt.Audience,
            Subject = new ClaimsIdentity(
            [
                new Claim(AuthClaimTypes.Subject, id.ToString()),
                new Claim(AuthClaimTypes.SecurityStamp, TokenHasher.Hash(stamp)),
            ]),
            IssuedAt = expires.AddMinutes(-60),
            NotBefore = expires.AddMinutes(-60),
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? opt.Key)), SecurityAlgorithms.HmacSha256),
        });
    }

    private async Task<T> GetUserAsync<T>(string email, Func<ApplicationUser, T> select)
    {
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        return select(user!);
    }
}
