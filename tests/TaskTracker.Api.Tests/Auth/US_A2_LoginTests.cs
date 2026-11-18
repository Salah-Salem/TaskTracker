using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Auth;
using TaskTracker.Domain;
using TaskTracker.Domain.Entities;

namespace TaskTracker.Api.Tests.Auth;

public class US_A2_LoginTests(TaskTrackerWebAppFactory factory) : IClassFixture<TaskTrackerWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Valid_Credentials_Return_Token_And_User_With_Roles()
    {
        var email = AuthApi.NewEmail();
        (await _client.RegisterAsync(email)).EnsureSuccessStatusCode();

        var res = await _client.LoginAsync(email);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await res.ReadEnvelopeAsync<AuthResponse>()).Data!;
        auth.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        auth.RefreshToken.Should().NotBeNullOrWhiteSpace();
        auth.RefreshTokenExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
        auth.User.Email.Should().Be(email);
        auth.User.Roles.Should().Equal(Roles.User);
    }

    [Fact]
    public async Task Wrong_Password_And_Unknown_Email_Return_Same_Generic_401()
    {
        var email = AuthApi.NewEmail();
        (await _client.RegisterAsync(email)).EnsureSuccessStatusCode();

        var wrongPassword = await _client.LoginAsync(email, "wrong-password");
        var unknownEmail = await _client.LoginAsync(AuthApi.NewEmail());

        foreach (var res in new[] { wrongPassword, unknownEmail })
        {
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var body = await res.ReadEnvelopeAsync<AuthResponse>();
            body.Data.Should().BeNull();
            body.Errors.Should().Equal("Invalid email or password");
        }
    }

    [Fact]
    public async Task Deactivated_Account_Is_Denied()
    {
        var email = AuthApi.NewEmail();
        (await _client.RegisterAsync(email)).EnsureSuccessStatusCode();
        await SetActiveAsync(email, false);

        var res = await _client.LoginAsync(email);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await res.ReadEnvelopeAsync<object>()).Errors.Should().Equal("Account disabled");
    }

    [Fact]
    public async Task Five_Failed_Attempts_Lock_The_Account()
    {
        var email = AuthApi.NewEmail();
        (await _client.RegisterAsync(email)).EnsureSuccessStatusCode();

        for (var i = 0; i < 5; i++)
            await _client.LoginAsync(email, "wrong-password");
        var res = await _client.LoginAsync(email); // correct password, but locked

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await res.ReadEnvelopeAsync<object>()).Errors.Should().ContainSingle(e => e.Contains("locked"));
    }

    private async Task SetActiveAsync(string email, bool active)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        user!.IsActive = active;
        await users.UpdateAsync(user);
    }
}
