using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Auth;
using TaskTracker.Domain;
using TaskTracker.Domain.Entities;

namespace TaskTracker.Api.Tests.Auth;

public class US_A1_RegisterTests(TaskTrackerWebAppFactory factory) : IClassFixture<TaskTrackerWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Valid_Input_Creates_Account_With_User_Role()
    {
        var email = AuthApi.NewEmail();

        var res = await _client.RegisterAsync(email, displayName: "Alice");

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        res.Headers.Location!.ToString().Should().Be("/api/auth/me");
        var body = await res.ReadEnvelopeAsync<UserDto>();
        body.Success.Should().BeTrue();
        body.Data!.Email.Should().Be(email);
        body.Data.DisplayName.Should().Be("Alice");
        body.Data.Roles.Should().Equal(Roles.User);
    }

    [Fact]
    public async Task Duplicate_Email_Returns_400_And_Creates_No_Second_Account()
    {
        var email = AuthApi.NewEmail();
        (await _client.RegisterAsync(email)).EnsureSuccessStatusCode();

        var res = await _client.RegisterAsync(email.ToUpperInvariant());

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.ReadEnvelopeAsync<object>()).Errors.Should().Equal("Email already registered");

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        users.Users.Count(u => u.NormalizedEmail == email.ToUpperInvariant()).Should().Be(1);
    }

    [Theory]
    [InlineData("not-an-email", AuthApi.Password, "Bob", "Email")]
    [InlineData("VALID", "short", "Bob", "Password")]
    [InlineData("VALID", AuthApi.Password, "", "Display Name")]
    [InlineData("", "", "", "Email")]
    public async Task Invalid_Input_Returns_400_With_Field_Errors(string email, string password, string name, string expectedField)
    {
        if (email == "VALID") email = AuthApi.NewEmail();

        var res = await _client.RegisterAsync(email, password, name);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.ReadEnvelopeAsync<object>();
        body.Success.Should().BeFalse();
        body.Errors.Should().Contain(e => e.Contains(expectedField));
    }

    [Fact]
    public async Task Password_Is_Hashed_And_Never_Returned()
    {
        var email = AuthApi.NewEmail();

        var res = await _client.RegisterAsync(email);
        var raw = await res.Content.ReadAsStringAsync();

        raw.Should().NotContain(AuthApi.Password).And.NotContainEquivalentOf("password");
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        user!.PasswordHash.Should().NotBeNullOrEmpty().And.NotBe(AuthApi.Password);
    }
}
