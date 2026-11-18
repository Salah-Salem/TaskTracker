using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Common;
using TaskTracker.Domain;
using F = TaskTracker.Api.Tests.Infrastructure.TaskTrackerWebAppFactory;

namespace TaskTracker.Api.Tests.Users;

/// <summary>
/// US-B2. Tests here never mutate the seeded admin/pm/tm accounts — they act on freshly registered users,
/// so they don't interfere with each other. The last-admin guard lives in its own class (own database).
/// </summary>
public class US_B2_UserManagementTests(F factory) : IClassFixture<F>
{
    [Fact]
    public async Task Admin_Sees_Paginated_Users_With_Name_Email_Roles_Status()
    {
        var admin = await factory.AuthenticateAsAdmin();

        var res = await admin.GetAsync("/api/users?page=1&pageSize=2");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await res.ReadEnvelopeAsync<PagedResult<UserDto>>()).Data!;
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(2);
        page.Items.Should().HaveCount(2);
        page.TotalCount.Should().BeGreaterThanOrEqualTo(5);
        page.Items.Should().OnlyContain(u => u.Email != "" && u.DisplayName != "" && u.Roles.Length == 1);
    }

    [Fact]
    public async Task Users_Are_Listed_Across_Pages_Without_Overlap()
    {
        var admin = await factory.AuthenticateAsAdmin();

        var p1 = (await (await admin.GetAsync("/api/users?page=1&pageSize=3")).ReadEnvelopeAsync<PagedResult<UserDto>>()).Data!;
        var p2 = (await (await admin.GetAsync("/api/users?page=2&pageSize=3")).ReadEnvelopeAsync<PagedResult<UserDto>>()).Data!;

        p1.Items.Select(u => u.Id).Should().NotIntersectWith(p2.Items.Select(u => u.Id));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Invalid_Paging_Returns_400(string qs)
    {
        var admin = await factory.AuthenticateAsAdmin();
        (await admin.GetAsync($"/api/users?{qs}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(F.AliceEmail)]
    [InlineData(F.CarolEmail)]
    public async Task Non_Admins_Get_403_On_Every_User_Endpoint(string email)
    {
        var client = await factory.AuthenticateAsync(email);
        var target = await RegisterUserAsync();

        var responses = new[]
        {
            await client.GetAsync("/api/users"),
            await client.PutAsJsonAsync($"/api/users/{target}/role", new { role = Roles.Admin }),
            await client.PutAsync($"/api/users/{target}/deactivate", null),
            await client.PutAsync($"/api/users/{target}/activate", null),
        };

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Forbidden);
        (await responses[0].ReadEnvelopeAsync<object>()).Errors.Should().Equal("Forbidden");
    }

    [Fact]
    public async Task Promoted_User_Gets_Admin_Rights_On_Next_Token_Via_Refresh()
    {
        var admin = await factory.AuthenticateAsAdmin();
        var anon = factory.CreateClient();
        var (_, before) = await anon.RegisterAndLoginAsync();
        (await factory.CreateClient().WithBearer(before.AccessToken).GetAsync("/api/users"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "a plain User is not an Admin yet");

        var res = await admin.PutAsJsonAsync($"/api/users/{before.User.Id}/role", new { role = Roles.Admin });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.ReadEnvelopeAsync<UserDto>()).Data!.Roles.Should().Equal(Roles.Admin);

        // Takes effect on the next request: the old access token (role=User) is rejected...
        (await factory.CreateClient().WithBearer(before.AccessToken).GetAsync("/api/auth/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // ...but the refresh token still works and yields an access token with the new role.
        var refreshed = await anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = before.RefreshToken });
        var promoted = (await refreshed.ReadEnvelopeAsync<AuthResponse>()).Data!;
        promoted.User.Roles.Should().Equal(Roles.Admin);
        new JsonWebToken(promoted.AccessToken).Claims.Where(c => c.Type == "role").Select(c => c.Value)
            .Should().Equal(Roles.Admin);
        (await factory.CreateClient().WithBearer(promoted.AccessToken).GetAsync("/api/users"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the new token passes the AdminOnly policy");
    }

    [Fact]
    public async Task Unchanged_Role_Is_A_NoOp_And_Keeps_Sessions()
    {
        var admin = await factory.AuthenticateAsAdmin();
        var (_, auth) = await factory.CreateClient().RegisterAndLoginAsync();

        var res = await admin.PutAsJsonAsync($"/api/users/{auth.User.Id}/role", new { role = Roles.User });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.CreateClient().WithBearer(auth.AccessToken).GetAsync("/api/auth/me"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("SuperUser")]
    [InlineData("admin")]
    [InlineData("")]
    public async Task Invalid_Role_Returns_400(string role)
    {
        var admin = await factory.AuthenticateAsAdmin();
        var target = await RegisterUserAsync();

        var res = await admin.PutAsJsonAsync($"/api/users/{target}/role", new { role });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.ReadEnvelopeAsync<object>()).Errors.Should().Contain(e => e.StartsWith("Role must be one of"));
    }

    [Fact]
    public async Task Unknown_User_Returns_404()
    {
        var admin = await factory.AuthenticateAsAdmin();
        var missing = Guid.NewGuid();

        (await admin.PutAsJsonAsync($"/api/users/{missing}/role", new { role = Roles.Admin })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsync($"/api/users/{missing}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsync($"/api/users/{missing}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deactivated_User_Gets_401_On_Next_Request_And_Can_Return_After_Activation()
    {
        var admin = await factory.AuthenticateAsAdmin();
        var anon = factory.CreateClient();
        var (email, auth) = await anon.RegisterAndLoginAsync();
        var user = factory.CreateClient().WithBearer(auth.AccessToken);
        (await user.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var deactivate = await admin.PutAsync($"/api/users/{auth.User.Id}/deactivate", null);

        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deactivate.ReadEnvelopeAsync<UserDto>()).Data!.IsActive.Should().BeFalse();
        (await user.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var activate = await admin.PutAsync($"/api/users/{auth.User.Id}/activate", null);

        activate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "pre-deactivation tokens stay dead");
        (await anon.LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> RegisterUserAsync()
    {
        var res = await factory.CreateClient().RegisterAsync(AuthApi.NewEmail());
        return (await res.ReadEnvelopeAsync<UserDto>()).Data!.Id;
    }
}
