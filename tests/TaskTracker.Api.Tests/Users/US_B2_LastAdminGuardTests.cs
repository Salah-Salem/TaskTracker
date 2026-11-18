using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Auth;
using TaskTracker.Domain;
using F = TaskTracker.Api.Tests.Infrastructure.TaskTrackerWebAppFactory;

namespace TaskTracker.Api.Tests.Users;

/// <summary>
/// US-B2 last-admin guard. Each test gets its own database (fresh factory) because the tests
/// change how many admins exist.
/// </summary>
public class US_B2_LastAdminGuardTests
{
    private const string LastAdmin = "At least one active Admin is required";

    [Fact]
    public async Task Last_Admin_Cannot_Demote_Themselves()
    {
        await using var factory = new F();
        var admin = await factory.AuthenticateAsAdmin();
        var adminId = await MyIdAsync(admin);

        var res = await admin.PutAsJsonAsync($"/api/users/{adminId}/role", new { role = Roles.User });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.ReadEnvelopeAsync<object>()).Errors.Should().Equal(LastAdmin);
        (await admin.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.OK, "still an admin, session intact");
    }

    [Fact]
    public async Task Last_Admin_Cannot_Deactivate_Themselves()
    {
        await using var factory = new F();
        var admin = await factory.AuthenticateAsAdmin();
        var adminId = await MyIdAsync(admin);

        var res = await admin.PutAsync($"/api/users/{adminId}/deactivate", null);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.ReadEnvelopeAsync<object>()).Errors.Should().Equal(LastAdmin);
    }

    [Fact]
    public async Task With_A_Second_Active_Admin_The_First_Can_Be_Demoted()
    {
        await using var factory = new F();
        var admin = await factory.AuthenticateAsAdmin();
        var adminId = await MyIdAsync(admin);
        var (email, other) = await factory.CreateClient().RegisterAndLoginAsync();
        (await admin.PutAsJsonAsync($"/api/users/{other.User.Id}/role", new { role = Roles.Admin }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var res = await admin.PutAsJsonAsync($"/api/users/{adminId}/role", new { role = Roles.User });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        // The second admin is now the last one and is protected in turn.
        var second = await factory.AuthenticateAsync(email);
        (await second.PutAsync($"/api/users/{other.User.Id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Inactive_Admins_Do_Not_Count_Toward_The_Minimum()
    {
        await using var factory = new F();
        var admin = await factory.AuthenticateAsAdmin();
        var adminId = await MyIdAsync(admin);
        var (_, other) = await factory.CreateClient().RegisterAndLoginAsync();
        await admin.PutAsJsonAsync($"/api/users/{other.User.Id}/role", new { role = Roles.Admin });
        (await admin.PutAsync($"/api/users/{other.User.Id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var res = await admin.PutAsJsonAsync($"/api/users/{adminId}/role", new { role = Roles.User });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> MyIdAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/me")).ReadEnvelopeAsync<UserDto>()).Data!.Id;
}
