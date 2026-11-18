using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TaskTracker.Application.Auth;

namespace TaskTracker.Api.Tests.Infrastructure;

/// <summary>
/// Runs the real API against a private in-memory SQLite database (Section 15).
/// The DB lives as long as <see cref="_keepAlive"/> is open, i.e. for the lifetime of the test class fixture.
/// Runs as Development, so migrations + demo seed data (admin, alice, bob, carol, dave) are applied.
/// </summary>
public class TaskTrackerWebAppFactory : WebApplicationFactory<Program>
{
    // Seeded accounts. alice = Owner, bob = Member, carol = Viewer of "Website Redesign"; dave = not a member.
    public const string AdminEmail = "admin@example.com";
    public const string AliceEmail = "alice@example.com";
    public const string BobEmail = "bob@example.com";
    public const string CarolEmail = "carol@example.com";
    public const string DaveEmail = "dave@example.com";

    private readonly string _connectionString =
        $"Data Source=tasktracker-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;

    public TaskTrackerWebAppFactory()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _connectionString,
            ["Serilog:MinimumLevel:Default"] = "Warning",
        }));
    }

    /// <summary>Logs in and returns the full auth payload (tokens + user).</summary>
    public async Task<AuthResponse> LoginAsync(string email, string password = AuthApi.Password)
    {
        var res = await CreateClient().LoginAsync(email, password);
        res.EnsureSuccessStatusCode();
        return (await res.ReadEnvelopeAsync<AuthResponse>()).Data!;
    }

    /// <summary>Logs in as <paramref name="email"/> and returns a client carrying that user's bearer token.</summary>
    public async Task<HttpClient> AuthenticateAsync(string email, string password = AuthApi.Password) =>
        CreateClient().WithBearer((await LoginAsync(email, password)).AccessToken);

    public Task<HttpClient> AuthenticateAsAdmin() => AuthenticateAsync(AdminEmail);
    public Task<HttpClient> AuthenticateAsOwner() => AuthenticateAsync(AliceEmail);
    public Task<HttpClient> AuthenticateAsMember() => AuthenticateAsync(BobEmail);
    public Task<HttpClient> AuthenticateAsViewer() => AuthenticateAsync(CarolEmail);
    public Task<HttpClient> AuthenticateAsNonMember() => AuthenticateAsync(DaveEmail);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}
