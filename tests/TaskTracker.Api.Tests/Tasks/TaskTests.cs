using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Common;
using TaskTracker.Application.Tasks;
using TaskTracker.Domain.Enums;
using F = TaskTracker.Api.Tests.Infrastructure.TaskTrackerWebAppFactory;
using TaskStatus = TaskTracker.Domain.Enums.TaskStatus;

namespace TaskTracker.Api.Tests.Tasks;

/// <summary>TASKS.md §4.2 matrix, §7 lifecycle (add / edit / delete / search) and §6.2–6.3 query options.</summary>
public class TaskTests(F factory) : IClassFixture<F>
{
    [Fact]
    public async Task Create_Defaults_And_Location_Header()
    {
        var p = await factory.CreateProjectWithCastAsync();

        var res = await p.Bob.PostAsJsonAsync(p.Tasks, new { title = "  Write tests  ", dueDate = "2030-11-15" });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await res.DataAsync<TaskDto>();
        res.Headers.Location!.ToString().Should().EndWith($"/api/projects/{p.ProjectId}/tasks/{task.Id}");
        task.Title.Should().Be("Write tests");
        task.Status.Should().Be(TaskStatus.Todo);
        task.Priority.Should().Be(TaskPriority.Medium);
        task.DueDate.Should().Be(new DateOnly(2030, 11, 15));
        task.CreatedById.Should().Be(p.BobId);
        task.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Role_Matrix_For_Tasks()
    {
        var p = await factory.CreateProjectWithCastAsync();
        var task = await p.CreateTaskAsync(p.Alice, new { title = "T" });
        var url = $"{p.Tasks}/{task.Id}";

        // read: everyone in the project; outsider 404
        foreach (var c in new[] { p.Alice, p.Bob, p.Carol }) (await c.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await p.Dave.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Dave.GetAsync(p.Tasks)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // create/edit: Owner + Member; Viewer 403; outsider 404
        (await p.Carol.PostAsJsonAsync(p.Tasks, new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await p.Carol.PatchAsJsonAsync(url, new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await p.Dave.PostAsJsonAsync(p.Tasks, new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Bob.PatchAsJsonAsync(url, new { title = "by bob" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // delete: Owner only
        (await p.Bob.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await p.Carol.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await p.Dave.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Alice.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await p.Alice.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, "second delete is a 404");
        (await p.Bob.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assignee_Rules_400_For_Non_Member_422_For_Viewer()
    {
        var p = await factory.CreateProjectWithCastAsync();

        (await p.Alice.PostAsJsonAsync(p.Tasks, new { title = "x", assigneeId = p.DaveId })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await p.Alice.PostAsJsonAsync(p.Tasks, new { title = "x", assigneeId = p.CarolId })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var task = await p.CreateTaskAsync(p.Alice, new { title = "x", assigneeId = p.BobId });
        (await p.Alice.PatchAsJsonAsync($"{p.Tasks}/{task.Id}", new { assigneeId = p.CarolId })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Patch_Is_Partial_Null_Unassigns_And_Done_Stamps_CompletedAt()
    {
        var p = await factory.CreateProjectWithCastAsync();
        var task = await p.CreateTaskAsync(p.Alice, new { title = "Keep me", description = "desc", assigneeId = p.BobId, dueDate = "2030-01-01" });
        var url = $"{p.Tasks}/{task.Id}";

        var done = await (await p.Bob.PatchAsJsonAsync(url, new { status = "Done" })).DataAsync<TaskDto>();
        done.Status.Should().Be(TaskStatus.Done);
        done.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        done.Title.Should().Be("Keep me");
        done.AssigneeId.Should().Be(p.BobId, "fields not sent stay as they were");

        var reopened = await (await p.Bob.PatchAsJsonAsync(url, new { status = "InReview", assigneeId = (Guid?)null, dueDate = (string?)null })).DataAsync<TaskDto>();
        reopened.CompletedAt.Should().BeNull("leaving Done clears completedAt");
        reopened.AssigneeId.Should().BeNull("explicit null unassigns");
        reopened.DueDate.Should().BeNull();
        reopened.Description.Should().Be("desc");
        reopened.UpdatedAt.Should().BeOnOrAfter(done.UpdatedAt);
    }

    [Theory]
    [InlineData("""{ "title": "" }""")]
    [InlineData("""{ "status": "Finished" }""")]
    [InlineData("""{ "priority": "Urgent" }""")]
    public async Task Invalid_Patch_Returns_400(string json)
    {
        var p = await factory.CreateProjectWithCastAsync();
        var task = await p.CreateTaskAsync(p.Alice, new { title = "x" });

        var res = await p.Alice.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{p.Tasks}/{task.Id}")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Task_From_Another_Project_Is_404()
    {
        var p1 = await factory.CreateProjectWithCastAsync();
        var p2 = await factory.CreateProjectWithCastAsync();
        var task = await p1.CreateTaskAsync(p1.Alice, new { title = "x" });

        (await p2.Alice.GetAsync($"{p2.Tasks}/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p2.Alice.DeleteAsync($"{p2.Tasks}/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_Filters_Sort_And_Paging_Combine()
    {
        var p = await factory.CreateProjectWithCastAsync();
        await p.CreateTaskAsync(p.Alice, new { title = "Fix LOGIN bug", priority = "High", assigneeId = p.BobId, dueDate = "2030-01-10" });
        await p.CreateTaskAsync(p.Alice, new { title = "Other", description = "about the login page", priority = "Low", dueDate = "2030-01-20" });
        await p.CreateTaskAsync(p.Alice, new { title = "Unrelated", priority = "Critical" });
        var doneOne = await p.CreateTaskAsync(p.Alice, new { title = "login done", priority = "Medium" });
        await p.Alice.PatchAsJsonAsync($"{p.Tasks}/{doneOne.Id}", new { status = "Done" });

        async Task<PagedResult<TaskDto>> Q(string qs) => await (await p.Carol.GetAsync($"{p.Tasks}?{qs}")).DataAsync<PagedResult<TaskDto>>();

        (await Q("search=login")).TotalCount.Should().Be(3, "title or description, case-insensitive");
        (await Q("search=login&status=Todo")).Items.Select(t => t.Title).Should().BeEquivalentTo("Fix LOGIN bug", "Other");
        (await Q("status=Todo&status=Done")).TotalCount.Should().Be(4);
        (await Q("priority=High,Critical")).Items.Select(t => t.Priority).Should().BeEquivalentTo([TaskPriority.High, TaskPriority.Critical]);
        (await Q($"assigneeId={p.BobId}")).Items.Should().ContainSingle().Which.Title.Should().Be("Fix LOGIN bug");
        (await Q("assigneeId=none")).TotalCount.Should().Be(3);
        (await Q("dueAfter=2030-01-15&dueBefore=2030-01-31")).Items.Should().ContainSingle().Which.Title.Should().Be("Other");
        (await Q("sortBy=priority&sortDir=desc")).Items.Select(t => t.Priority).Should().BeInDescendingOrder();
        (await Q("sortBy=title&sortDir=asc")).Items.Select(t => t.Title).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        var page2 = await Q("pageSize=3&page=2");
        page2.Items.Should().ContainSingle();
        page2.TotalCount.Should().Be(4);
        (await Q("search=nothing-matches-this")).Should().BeEquivalentTo(new { Items = Array.Empty<TaskDto>(), TotalCount = 0 });
    }

    [Theory]
    [InlineData("status=Finished")]
    [InlineData("priority=1")]
    [InlineData("assigneeId=bob")]
    [InlineData("sortBy=assignee")]
    [InlineData("dueAfter=2030-02-01&dueBefore=2030-01-01")]
    [InlineData("pageSize=0")]
    public async Task Invalid_Task_Query_Returns_400_Not_500(string qs)
    {
        var p = await factory.CreateProjectWithCastAsync();
        (await p.Bob.GetAsync($"{p.Tasks}?{qs}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("'; DROP TABLE Tasks; --")]
    [InlineData("\\")]
    public async Task Special_Characters_In_Search_Are_Matched_Literally(string term)
    {
        var p = await factory.CreateProjectWithCastAsync();
        await p.CreateTaskAsync(p.Alice, new { title = "plain title" });
        await p.CreateTaskAsync(p.Alice, new { title = $"has {term} inside" });

        var res = await p.Alice.GetAsync($"{p.Tasks}?search={Uri.EscapeDataString(term)}");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.DataAsync<PagedResult<TaskDto>>()).Items.Should().ContainSingle().Which.Title.Should().Contain(term);
    }

    [Fact]
    public async Task My_Tasks_Spans_Projects_And_Only_Shows_My_Assignments()
    {
        var p1 = await factory.CreateProjectWithCastAsync("Proj One");
        var p2 = await factory.CreateProjectWithCastAsync("Proj Two");
        var mine1 = await p1.CreateTaskAsync(p1.Alice, new { title = "mine in one", assigneeId = p1.BobId });
        var mine2 = await p2.CreateTaskAsync(p2.Alice, new { title = "mine in two", assigneeId = p2.BobId });
        await p1.CreateTaskAsync(p1.Alice, new { title = "not mine", assigneeId = p1.AliceId });

        var page = await (await p1.Bob.GetAsync("/api/me/tasks?pageSize=100")).DataAsync<PagedResult<TaskDto>>();

        page.Items.Should().OnlyContain(t => t.AssigneeId == p1.BobId);
        page.Items.Should().Contain(t => t.Id == mine1.Id && t.ProjectName == "Proj One");
        page.Items.Should().Contain(t => t.Id == mine2.Id && t.ProjectName == "Proj Two");

        // Leaving a project removes its tasks from my list.
        await p2.Alice.DeleteAsync($"{p2.Url}/members/{p2.BobId}");
        var after = await (await p1.Bob.GetAsync("/api/me/tasks?pageSize=100")).DataAsync<PagedResult<TaskDto>>();
        after.Items.Should().NotContain(t => t.Id == mine2.Id);

        (await factory.CreateClient().GetAsync("/api/me/tasks")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
