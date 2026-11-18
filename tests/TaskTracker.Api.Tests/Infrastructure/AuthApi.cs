using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using TaskTracker.Application.Auth;

namespace TaskTracker.Api.Tests.Infrastructure;

/// <summary>Test-side view of the API envelope.</summary>
public record Envelope<T>(bool Success, T? Data, string[] Errors);

public static class AuthApi
{
    public const string Password = "Passw0rd!";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@test.local";

    public static Task<HttpResponseMessage> RegisterAsync(
        this HttpClient c, string email, string password = Password, string displayName = "Test User") =>
        c.PostAsJsonAsync("/api/auth/register", new { email, password, displayName });

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient c, string email, string password = Password) =>
        c.PostAsJsonAsync("/api/auth/login", new { email, password });

    public static async Task<Envelope<T>> ReadEnvelopeAsync<T>(this HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<Envelope<T>>(ProjectApi.Json))!;

    /// <summary>Registers a fresh user, logs in, returns the token.</summary>
    public static async Task<(string Email, AuthResponse Auth)> RegisterAndLoginAsync(this HttpClient c)
    {
        var email = NewEmail();
        (await c.RegisterAsync(email)).EnsureSuccessStatusCode();
        var login = await c.LoginAsync(email);
        login.EnsureSuccessStatusCode();
        var auth = (await login.ReadEnvelopeAsync<AuthResponse>()).Data;
        auth.Should().NotBeNull();
        return (email, auth!);
    }

    public static HttpClient WithBearer(this HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }
}
