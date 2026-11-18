using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Common;
using TaskTracker.Application.Projects;
using TaskTracker.Application.Tasks;
using TaskTracker.Domain.Enums;
using F = TaskTracker.Api.Tests.Infrastructure.TaskTrackerWebAppFactory;

namespace TaskTracker.Api.Tests.Projects;

/// <summary>TASKS.md §4.1 permission matrix + member-management invariants (409).</summary>
public class ProjectAndMembershipTests(F factory) : IClassFixture<F>
{
    [Fact]
    public async Task Any_User_Can_Create_A_Project_And_Becomes_Owner()
    {
        var (_, auth) = await factory.CreateClient().RegisterAndLoginAsync();
        var client = factory.CreateClient().WithBearer(auth.AccessToken);

        var res = await client.PostAsJsonAsync("/api/projects", new { name = "Mine", description = "x" });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        res.Headers.Location!.ToString().Should().EndWith($"/api/projects/{(await res.DataAsync<ProjectDto>()).Id}");
        var members = await (await client.GetAsync($"{res.Headers.Location}/members")).DataAsync<List<MemberDto>>();
        members.Should().ContainSingle().Which.Role.Should().Be(ProjectRole.Owner);
    }

    [Theory]
    [InlineData("", 400)]
    [InlineData("A name that is far too long ____________________________________________________________________________", 400)]
    public async Task Invalid_Project_Name_Returns_400(string name, int expected)
    {
        var alice = await factory.AuthenticateAsOwner();
        ((int)(await alice.PostAsJsonAsync("/api/projects", new { name })).StatusCode).Should().Be(expected);
    }

    [Fact]
    public async Task Non_Member_Gets_404_Everywhere_Members_Get_Read_Access()
    {
        var p = await factory.CreateProjectWithCastAsync();

        foreach (var (client, expected) in new[] { (p.Alice, 200), (p.Bob, 200), (p.Carol, 200), (p.Dave, 404) })
        {
            ((int)(await client.GetAsync(p.Url)).StatusCode).Should().Be(expected);
            ((int)(await client.GetAsync($"{p.Url}/members")).StatusCode).Should().Be(expected);
        }
        (await p.Dave.PatchAsJsonAsync(p.Url, new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Dave.DeleteAsync(p.Url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_Owner_Can_Update_Delete_Or_Manage_Members()
    {
        var p = await factory.CreateProjectWithCastAsync();

        foreach (var client in new[] { p.Bob, p.Carol })
        {
            (await client.PatchAsJsonAsync(p.Url, new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.DeleteAsync(p.Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync($"{p.Url}/members", new { email = F.DaveEmail, role = "Viewer" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.PatchAsJsonAsync($"{p.Url}/members/{p.CarolId}", new { role = "Member" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.DeleteAsync($"{p.Url}/members/{p.CarolId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync($"{p.Url}/transfer-ownership", new { newOwnerUserId = p.BobId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Patch_Only_Changes_Fields_That_Are_Sent()
    {
        var p = await factory.CreateProjectWithCastAsync("Original");

        var renamed = await (await p.Alice.PatchAsJsonAsync(p.Url, new { name = "Renamed" })).DataAsync<ProjectDto>();
        renamed.Name.Should().Be("Renamed");
        renamed.Description.Should().Be("fixture", "description was not sent, so it is unchanged");

        var cleared = await (await p.Alice.PatchAsJsonAsync(p.Url, new { description = (string?)null })).DataAsync<ProjectDto>();
        cleared.Name.Should().Be("Renamed");
        cleared.Description.Should().BeNull();

        (await p.Alice.PatchAsJsonAsync(p.Url, new { name = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Membership_Invariants_Return_409()
    {
        var p = await factory.CreateProjectWithCastAsync();

        (await p.Alice.PostAsJsonAsync($"{p.Url}/members", new { email = F.BobEmail, role = "Viewer" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "already a member");
        (await p.Alice.PostAsJsonAsync($"{p.Url}/members", new { email = F.DaveEmail, role = "Owner" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "Owner only via transfer");
        (await p.Alice.PatchAsJsonAsync($"{p.Url}/members/{p.BobId}", new { role = "Owner" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "Owner only via transfer");
        (await p.Alice.PatchAsJsonAsync($"{p.Url}/members/{p.AliceId}", new { role = "Member" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "cannot demote the last Owner");
        (await p.Alice.DeleteAsync($"{p.Url}/members/{p.AliceId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "cannot remove the last Owner");
    }

    [Fact]
    public async Task Member_Management_Edge_Cases()
    {
        var p = await factory.CreateProjectWithCastAsync();

        (await p.Alice.PostAsJsonAsync($"{p.Url}/members", new { email = "nobody@example.com", role = "Member" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Alice.PostAsJsonAsync($"{p.Url}/members", new { email = F.DaveEmail, role = "Boss" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "invalid enum string");
        (await p.Alice.DeleteAsync($"{p.Url}/members/{p.DaveId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Alice.PostAsJsonAsync($"{p.Url}/transfer-ownership", new { newOwnerUserId = p.DaveId }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "new owner must already be a member");
    }

    [Fact]
    public async Task Demoting_To_Viewer_Or_Removing_A_Member_Unassigns_Their_Tasks()
    {
        var p = await factory.CreateProjectWithCastAsync();
        var t1 = await p.CreateTaskAsync(p.Alice, new { title = "A", assigneeId = p.BobId });

        var demoted = await p.Alice.PatchAsJsonAsync($"{p.Url}/members/{p.BobId}", new { role = "Viewer" });

        demoted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await p.Alice.GetAsync($"{p.Tasks}/{t1.Id}")).DataAsync<TaskDto>()).AssigneeId.Should().BeNull();

        await p.Alice.PatchAsJsonAsync($"{p.Url}/members/{p.BobId}", new { role = "Member" });
        var t2 = await p.CreateTaskAsync(p.Alice, new { title = "B", assigneeId = p.BobId });
        (await p.Alice.DeleteAsync($"{p.Url}/members/{p.BobId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await p.Alice.GetAsync($"{p.Tasks}/{t2.Id}")).DataAsync<TaskDto>()).AssigneeId.Should().BeNull();
        (await p.Bob.GetAsync(p.Url)).StatusCode.Should().Be(HttpStatusCode.NotFound, "removed members lose access");
    }

    [Fact]
    public async Task List_Is_Scoped_To_My_Projects_With_Search_Role_Filter_And_Sort()
    {
        var p = await factory.CreateProjectWithCastAsync($"Searchable {Guid.NewGuid():N}");

        var bobs = await (await p.Bob.GetAsync("/api/projects?pageSize=100")).DataAsync<PagedResult<ProjectDto>>();
        var daves = await (await p.Dave.GetAsync("/api/projects?pageSize=100")).DataAsync<PagedResult<ProjectDto>>();
        bobs.Items.Should().Contain(x => x.Id == p.ProjectId && x.MyRole == ProjectRole.Member);
        daves.Items.Should().NotContain(x => x.Id == p.ProjectId);

        var search = await (await p.Carol.GetAsync("/api/projects?search=SEARCHABLE")).DataAsync<PagedResult<ProjectDto>>();
        search.Items.Should().OnlyContain(x => x.Name.StartsWith("Searchable")).And.Contain(x => x.Id == p.ProjectId);

        var owned = await (await p.Bob.GetAsync("/api/projects?role=Owner&pageSize=100")).DataAsync<PagedResult<ProjectDto>>();
        owned.Items.Should().OnlyContain(x => x.MyRole == ProjectRole.Owner);

        var byName = await (await p.Alice.GetAsync("/api/projects?sortBy=name&sortDir=asc&pageSize=100")).DataAsync<PagedResult<ProjectDto>>();
        byName.Items.Select(x => x.Name).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("sortBy=owner")]
    [InlineData("sortDir=sideways")]
    [InlineData("role=King")]
    public async Task Invalid_List_Query_Returns_400(string qs)
    {
        var alice = await factory.AuthenticateAsOwner();
        (await alice.GetAsync($"/api/projects?{qs}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deleted_Project_Disappears_For_Everyone()
    {
        var p = await factory.CreateProjectWithCastAsync();

        (await p.Alice.DeleteAsync(p.Url)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await p.Alice.GetAsync(p.Url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Bob.GetAsync(p.Tasks)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await p.Alice.DeleteAsync(p.Url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task System_Admin_Acts_As_Owner_On_Any_Project()
    {
        var p = await factory.CreateProjectWithCastAsync();

        var asAdmin = await (await p.Admin.GetAsync(p.Url)).DataAsync<ProjectDto>();
        asAdmin.MyRole.Should().Be(ProjectRole.Owner);
        (await p.Admin.PatchAsJsonAsync(p.Url, new { name = "Moderated" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await p.Admin.PostAsJsonAsync($"{p.Url}/members", new { email = F.DaveEmail, role = "Viewer" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
