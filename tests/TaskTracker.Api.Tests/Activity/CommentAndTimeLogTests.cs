using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Activity;
using TaskTracker.Application.Common;
using F = TaskTracker.Api.Tests.Infrastructure.TaskTrackerWebAppFactory;

namespace TaskTracker.Api.Tests.Activity;

/// <summary>TASKS.md §4.3 / §4.4: Viewers read-only; edit/delete by author or project Owner.</summary>
public class CommentAndTimeLogTests(F factory) : IClassFixture<F>
{
    private async Task<(ProjectFixture P, string Comments, string Logs)> SetupAsync()
    {
        var p = await factory.CreateProjectWithCastAsync();
        var task = await p.CreateTaskAsync(p.Alice, new { title = "T" });
        var baseUrl = $"{p.Tasks}/{task.Id}";
        return (p, $"{baseUrl}/comments", $"{baseUrl}/timelogs");
    }

    [Fact]
    public async Task Comment_Permissions()
    {
        var (p, url, _) = await SetupAsync();

        (await p.Carol.PostAsJsonAsync(url, new { body = "hi" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "Viewers are read-only");
        (await p.Dave.PostAsJsonAsync(url, new { body = "hi" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var bobs = await (await p.Bob.PostAsJsonAsync(url, new { body = "bob's" })).DataAsync<CommentDto>();
        var alices = await (await p.Alice.PostAsJsonAsync(url, new { body = "alice's" })).DataAsync<CommentDto>();
        bobs.AuthorId.Should().Be(p.BobId);

        (await p.Bob.PatchAsJsonAsync($"{url}/{bobs.Id}", new { body = "edited" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await p.Bob.PatchAsJsonAsync($"{url}/{alices.Id}", new { body = "nope" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await p.Carol.DeleteAsync($"{url}/{bobs.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await p.Dave.DeleteAsync($"{url}/{bobs.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Alice.PatchAsJsonAsync($"{url}/{bobs.Id}", new { body = "moderated" })).StatusCode.Should().Be(HttpStatusCode.OK, "Owner moderation");

        var list = await (await p.Carol.GetAsync(url)).DataAsync<PagedResult<CommentDto>>();
        list.Items.Select(c => c.Id).Should().Equal([alices.Id, bobs.Id], "newest first");
        list.Items.Last().Body.Should().Be("moderated");

        (await p.Bob.DeleteAsync($"{url}/{bobs.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await p.Alice.DeleteAsync($"{url}/{bobs.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Alice.PostAsJsonAsync(url, new { body = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Viewer_Who_Authored_A_Comment_Before_Demotion_Can_Still_Edit_It()
    {
        var (p, url, _) = await SetupAsync();
        var bobs = await (await p.Bob.PostAsJsonAsync(url, new { body = "while member" })).DataAsync<CommentDto>();
        await p.Alice.PatchAsJsonAsync($"{p.Url}/members/{p.BobId}", new { role = "Viewer" });

        (await p.Bob.PatchAsJsonAsync($"{url}/{bobs.Id}", new { body = "still mine" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await p.Bob.PostAsJsonAsync(url, new { body = "new" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TimeLog_Permissions_And_Caller_Is_Always_The_User()
    {
        var (p, _, url) = await SetupAsync();

        (await p.Carol.PostAsJsonAsync(url, new { minutes = 30 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A smuggled userId is ignored — the log belongs to the caller.
        var bobs = await (await p.Bob.PostAsJsonAsync(url, new { minutes = 30, userId = p.AliceId })).DataAsync<TimeLogDto>();
        bobs.UserId.Should().Be(p.BobId);
        bobs.WorkDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow), "defaults to today (UTC)");
        var alices = await (await p.Alice.PostAsJsonAsync(url, new { minutes = 15, note = "n" })).DataAsync<TimeLogDto>();

        var edited = await (await p.Bob.PatchAsJsonAsync($"{url}/{bobs.Id}", new { minutes = 45 })).DataAsync<TimeLogDto>();
        edited.Minutes.Should().Be(45);
        (await p.Bob.PatchAsJsonAsync($"{url}/{alices.Id}", new { minutes = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var moderated = await (await p.Alice.PatchAsJsonAsync($"{url}/{bobs.Id}", new { note = "checked" })).DataAsync<TimeLogDto>();
        moderated.Minutes.Should().Be(45, "partial update keeps unsent fields");
        moderated.Note.Should().Be("checked");

        (await p.Alice.DeleteAsync($"{url}/{bobs.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1441, null)]
    [InlineData(30, "2999-01-01")]
    public async Task TimeLog_Validation(int minutes, string? workDate)
    {
        var (p, _, url) = await SetupAsync();
        (await p.Bob.PostAsJsonAsync(url, new { minutes, workDate })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Comments_And_Logs_Of_A_Deleted_Task_Are_Gone()
    {
        var (p, comments, logs) = await SetupAsync();
        await p.Bob.PostAsJsonAsync(comments, new { body = "x" });
        await p.Bob.PostAsJsonAsync(logs, new { minutes = 5 });
        var taskUrl = comments[..comments.LastIndexOf('/')];

        await p.Alice.DeleteAsync(taskUrl);

        (await p.Alice.GetAsync(comments)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Alice.GetAsync(logs)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
