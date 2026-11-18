using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Auth;
using F = TaskTracker.Api.Tests.Infrastructure.TaskTrackerWebAppFactory;

namespace TaskTracker.Api.Tests.Auth;

public class RefreshTokenTests(F factory) : IClassFixture<F>
{
    private readonly HttpClient _anon = factory.CreateClient();

    private Task<HttpResponseMessage> RefreshAsync(string token) =>
        _anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token });

    [Fact]
    public async Task Refresh_Returns_A_New_Working_Pair_And_Rotates_The_Old_Token()
    {
        var (_, login) = await _anon.RegisterAndLoginAsync();

        var res = await RefreshAsync(login.RefreshToken);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var next = (await res.ReadEnvelopeAsync<AuthResponse>()).Data!;
        next.RefreshToken.Should().NotBe(login.RefreshToken);
        (await factory.CreateClient().WithBearer(next.AccessToken).GetAsync("/api/auth/me"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await RefreshAsync(next.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "the new token is usable once");
    }

    [Fact]
    public async Task Reusing_A_Rotated_Token_Is_Rejected_And_Revokes_The_Whole_Chain()
    {
        var (_, login) = await _anon.RegisterAndLoginAsync();
        var next = (await (await RefreshAsync(login.RefreshToken)).ReadEnvelopeAsync<AuthResponse>()).Data!;

        var replay = await RefreshAsync(login.RefreshToken); // stolen-token replay

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(next.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "reuse detection revokes every outstanding refresh token for the user");
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("")]
    public async Task Unknown_Or_Empty_Token_Is_Rejected(string token)
    {
        var res = await RefreshAsync(token);
        res.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
        (await res.ReadEnvelopeAsync<object>()).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Logout_Revokes_Refresh_Tokens_Too()
    {
        var (_, login) = await _anon.RegisterAndLoginAsync();

        await factory.CreateClient().WithBearer(login.AccessToken).PostAsync("/api/auth/logout", null);

        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivated_User_Cannot_Refresh_Even_After_Reactivation()
    {
        var admin = await factory.AuthenticateAsAdmin();
        var (_, login) = await _anon.RegisterAndLoginAsync();

        await admin.PutAsync($"/api/users/{login.User.Id}/deactivate", null);
        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await admin.PutAsync($"/api/users/{login.User.Id}/activate", null);
        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "deactivation revoked it; reactivation does not revive old sessions");
    }

    [Fact]
    public async Task Refresh_Response_Never_Contains_The_Stored_Hash_Or_Password()
    {
        var (_, login) = await _anon.RegisterAndLoginAsync();
        var raw = await (await RefreshAsync(login.RefreshToken)).Content.ReadAsStringAsync();

        raw.Should().NotContainEquivalentOf("password").And.NotContainEquivalentOf("tokenHash").And.NotContainEquivalentOf("securityStamp");
    }
}
