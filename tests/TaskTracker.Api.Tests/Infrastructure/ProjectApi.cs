using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Projects;
using TaskTracker.Application.Tasks;
using TaskTracker.Domain.Enums;
using F = TaskTracker.Api.Tests.Infrastructure.TaskTrackerWebAppFactory;

namespace TaskTracker.Api.Tests.Infrastructure;

/// <summary>
/// A fresh project with the TASKS.md cast: alice = Owner, bob = Member, carol = Viewer, dave = outsider.
/// Each test builds its own so tests never interfere through shared data.
/// </summary>
public sealed record ProjectFixture(
    Guid ProjectId,
    HttpClient Alice, HttpClient Bob, HttpClient Carol, HttpClient Dave, HttpClient Admin,
    Guid AliceId, Guid BobId, Guid CarolId, Guid DaveId)
{
    public string Url => $"/api/projects/{ProjectId}";
    public string Tasks => $"{Url}/tasks";
}

public static class ProjectApi
{
    /// <summary>Same JSON settings as the API (camelCase + enums as strings) for reading responses.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<Envelope<T>> ReadAsync<T>(this HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<Envelope<T>>(Json))!;

    public static async Task<T> DataAsync<T>(this HttpResponseMessage res)
    {
        var body = await res.ReadAsync<T>();
        body.Success.Should().BeTrue($"expected success but got {(int)res.StatusCode}: {string.Join("; ", body.Errors)}");
        return body.Data!;
    }

    public static async Task<ProjectFixture> CreateProjectWithCastAsync(this F factory, string name = "Test Project")
    {
        var alice = await factory.LoginAsync(F.AliceEmail);
        var bob = await factory.LoginAsync(F.BobEmail);
        var carol = await factory.LoginAsync(F.CarolEmail);
        var dave = await factory.LoginAsync(F.DaveEmail);
        var admin = await factory.LoginAsync(F.AdminEmail);
        var aliceClient = factory.CreateClient().WithBearer(alice.AccessToken);

        var created = await aliceClient.PostAsJsonAsync("/api/projects", new { name, description = "fixture" });
        var project = await created.DataAsync<ProjectDto>();

        (await aliceClient.PostAsJsonAsync($"/api/projects/{project.Id}/members", new { email = F.BobEmail, role = "Member" })).EnsureSuccessStatusCode();
        (await aliceClient.PostAsJsonAsync($"/api/projects/{project.Id}/members", new { email = F.CarolEmail, role = "Viewer" })).EnsureSuccessStatusCode();

        return new ProjectFixture(project.Id,
            aliceClient,
            factory.CreateClient().WithBearer(bob.AccessToken),
            factory.CreateClient().WithBearer(carol.AccessToken),
            factory.CreateClient().WithBearer(dave.AccessToken),
            factory.CreateClient().WithBearer(admin.AccessToken),
            alice.User.Id, bob.User.Id, carol.User.Id, dave.User.Id);
    }

    public static async Task<TaskDto> CreateTaskAsync(this ProjectFixture p, HttpClient as_, object body)
    {
        var res = await as_.PostAsJsonAsync(p.Tasks, body);
        return await res.DataAsync<TaskDto>();
    }

    public static Task<HttpResponseMessage> PatchAsJsonAsync(this HttpClient c, string url, object body) =>
        c.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(body) });

    public static Guid UserId(this AuthResponse auth) => auth.User.Id;
}
