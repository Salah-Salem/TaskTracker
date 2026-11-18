using System.Net;
using FluentAssertions;
using TaskTracker.Api.Tests.Infrastructure;

namespace TaskTracker.Api.Tests.Auth;

public class US_A3_LogoutTests(TaskTrackerWebAppFactory factory) : IClassFixture<TaskTrackerWebAppFactory>
{
    [Fact]
    public async Task Token_Is_Rejected_After_Logout()
    {
        var (_, auth) = await factory.CreateClient().RegisterAndLoginAsync();
        var client = factory.CreateClient().WithBearer(auth.AccessToken);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var logout = await client.PostAsync("/api/auth/logout", null);
        var reuse = await client.GetAsync("/api/auth/me");

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_Invalidates_All_Sessions_Of_The_User()
    {
        var anon = factory.CreateClient();
        var (email, first) = await anon.RegisterAndLoginAsync();
        var second = (await (await anon.LoginAsync(email)).ReadEnvelopeAsync<TaskTracker.Application.Auth.AuthResponse>()).Data!;

        await factory.CreateClient().WithBearer(first.AccessToken).PostAsync("/api/auth/logout", null);

        (await factory.CreateClient().WithBearer(second.AccessToken).GetAsync("/api/auth/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_Without_Token_Returns_401()
    {
        var res = await factory.CreateClient().PostAsync("/api/auth/logout", null);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
