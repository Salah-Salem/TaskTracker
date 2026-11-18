using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Activity;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Common;
using TaskTracker.Application.Projects;
using TaskTracker.Application.Tasks;
using TaskTracker.Domain.Entities;
using TaskTracker.Domain.Enums;
using TaskTracker.Infrastructure.Auth;
using TaskTracker.Infrastructure.Persistence;
using F = TaskTracker.Api.Tests.Infrastructure.TaskTrackerWebAppFactory;

namespace TaskTracker.Api.Tests.Acceptance;

/// <summary>
/// TASKS.md §10 "Acceptance test scenarios", one test per row (#1–#20).
/// Each builds its own project (alice Owner, bob Member, carol Viewer, dave outsider) so they run independently.
/// </summary>
public class TasksMdAcceptanceTests(F factory) : IClassFixture<F>
{
    [Fact]
    public async Task S01_Anonymous_List_Projects_401() =>
        (await factory.CreateClient().GetAsync("/api/projects")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task S02_Alice_Creates_Project_And_Is_Owner()
    {
        var alice = await factory.AuthenticateAsOwner();
        var res = await alice.PostAsJsonAsync("/api/projects", new { name = "S02" });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        (await res.DataAsync<ProjectDto>()).MyRole.Should().Be(ProjectRole.Owner);
    }

    [Fact]
    public async Task S03_Alice_Adds_Bob_As_Member_And_Carol_As_Viewer()
    {
        var alice = await factory.AuthenticateAsOwner();
        var project = await (await alice.PostAsJsonAsync("/api/projects", new { name = "S03" })).DataAsync<ProjectDto>();

        var bob = await alice.PostAsJsonAsync($"/api/projects/{project.Id}/members", new { email = F.BobEmail, role = "Member" });
        var carol = await alice.PostAsJsonAsync($"/api/projects/{project.Id}/members", new { email = F.CarolEmail, role = "Viewer" });

        bob.StatusCode.Should().Be(HttpStatusCode.Created);
        carol.StatusCode.Should().Be(HttpStatusCode.Created);
        var members = await (await alice.GetAsync($"/api/projects/{project.Id}/members")).DataAsync<List<MemberDto>>();
        members.Select(m => (m.Email, m.Role)).Should().BeEquivalentTo(new[]
        {
            (F.AliceEmail, ProjectRole.Owner), (F.BobEmail, ProjectRole.Member), (F.CarolEmail, ProjectRole.Viewer),
        });
    }

    [Fact]
    public async Task S04_Dave_Non_Member_Gets_404()
    {
        var p = await factory.CreateProjectWithCastAsync();
        (await p.Dave.GetAsync(p.Url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task S05_Carol_Viewer_Cannot_Create_Task_403()
    {
        var p = await factory.CreateProjectWithCastAsync();
        (await p.Carol.PostAsJsonAsync(p.Tasks, new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task S06_Bob_Member_Creates_Task_201()
    {
        var p = await factory.CreateProjectWithCastAsync();
        (await p.Bob.PostAsJsonAsync(p.Tasks, new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task S07_Bob_Sets_Status_Done_And_CompletedAt_Is_Set()
    {
        var p = await factory.CreateProjectWithCastAsync();
        var task = await p.CreateTaskAsync(p.Bob, new { title = "x" });

        var res = await p.Bob.PatchAsJsonAsync($"{p.Tasks}/{task.Id}", new { status = "Done" });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.DataAsync<TaskDto>()).CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task S08_Bob_Cannot_Delete_Task_403()
    {
        var p = await factory.CreateProjectWithCastAsync();
        var task = await p.CreateTaskAsync(p.Bob, new { title = "x" });
        (await p.Bob.DeleteAsync($"{p.Tasks}/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task S09_Alice_Deletes_Task_204()
    {
        var p = await factory.CreateProjectWithCastAsync();
        var task = await p.CreateTaskAsync(p.Bob, new { title = "x" });
        (await p.Alice.DeleteAsync($"{p.Tasks}/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task S10_Carol_Cannot_Comment_403()
    {
        var (p, comments) = await ProjectWithTaskAsync("comments");
        (await p.Carol.PostAsJsonAsync(comments, new { body = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task S11_Bob_Creates_Then_Edits_Own_Comment()
    {
        var (p, comments) = await ProjectWithTaskAsync("comments");
        var created = await p.Bob.PostAsJsonAsync(comments, new { body = "first" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.DataAsync<CommentDto>()).Id;

        (await p.Bob.PatchAsJsonAsync($"{comments}/{id}", new { body = "second" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task S12_Bob_Cannot_Edit_Alices_Comment_403()
    {
        var (p, comments) = await ProjectWithTaskAsync("comments");
        var id = (await (await p.Alice.PostAsJsonAsync(comments, new { body = "alice" })).DataAsync<CommentDto>()).Id;
        (await p.Bob.PatchAsJsonAsync($"{comments}/{id}", new { body = "bob" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task S13_Alice_Owner_Edits_Bobs_Comment_200()
    {
        var (p, comments) = await ProjectWithTaskAsync("comments");
        var id = (await (await p.Bob.PostAsJsonAsync(comments, new { body = "bob" })).DataAsync<CommentDto>()).Id;
        (await p.Alice.PatchAsJsonAsync($"{comments}/{id}", new { body = "moderated" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task S14_Bob_Edits_Own_TimeLog_200()
    {
        var (p, logs) = await ProjectWithTaskAsync("timelogs");
        var id = (await (await p.Bob.PostAsJsonAsync(logs, new { minutes = 30 })).DataAsync<TimeLogDto>()).Id;
        (await p.Bob.PatchAsJsonAsync($"{logs}/{id}", new { minutes = 60 })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task S15_Alice_Owner_Edits_Bobs_TimeLog_200()
    {
        var (p, logs) = await ProjectWithTaskAsync("timelogs");
        var id = (await (await p.Bob.PostAsJsonAsync(logs, new { minutes = 30 })).DataAsync<TimeLogDto>()).Id;
        (await p.Alice.PatchAsJsonAsync($"{logs}/{id}", new { minutes = 20 })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task S16_Search_Login_And_Status_Todo_On_Seeded_Project()
    {
        var alice = await factory.AuthenticateAsOwner();
        var seeded = (await (await alice.GetAsync($"/api/projects?search={Uri.EscapeDataString(DbInitializer.DemoProjectName)}"))
            .DataAsync<PagedResult<ProjectDto>>()).Items.Single();

        var res = await alice.GetAsync($"/api/projects/{seeded.Id}/tasks?search=login&status=Todo");

        var page = await res.DataAsync<PagedResult<TaskDto>>();
        page.Items.Should().ContainSingle().Which.Title.Should().Be("Fix login redirect bug",
            "'Write copy for login page' matches the text but is InReview");
    }

    [Fact]
    public async Task S17_Alice_Cannot_Remove_Herself_As_Sole_Owner_409()
    {
        var p = await factory.CreateProjectWithCastAsync();
        (await p.Alice.DeleteAsync($"{p.Url}/members/{p.AliceId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task S18_Transfer_Ownership_To_Bob_Then_Alice_Cannot_Delete_Task()
    {
        var p = await factory.CreateProjectWithCastAsync();
        var task = await p.CreateTaskAsync(p.Alice, new { title = "x" });

        var res = await p.Alice.PostAsJsonAsync($"{p.Url}/transfer-ownership", new { newOwnerUserId = p.BobId });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var members = await res.DataAsync<List<MemberDto>>();
        members.Single(m => m.UserId == p.BobId).Role.Should().Be(ProjectRole.Owner);
        members.Single(m => m.UserId == p.AliceId).Role.Should().Be(ProjectRole.Member);
        (await p.Alice.DeleteAsync($"{p.Tasks}/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await p.Bob.DeleteAsync($"{p.Tasks}/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task S19_My_Tasks_As_Bob_Only_Assigned_To_Bob_With_ProjectName()
    {
        var p = await factory.CreateProjectWithCastAsync("S19 project");
        await p.CreateTaskAsync(p.Alice, new { title = "for bob", assigneeId = p.BobId });
        await p.CreateTaskAsync(p.Alice, new { title = "for alice", assigneeId = p.AliceId });

        var page = await (await p.Bob.GetAsync("/api/me/tasks?pageSize=100")).DataAsync<PagedResult<TaskDto>>();

        page.Items.Should().NotBeEmpty().And.OnlyContain(t => t.AssigneeId == p.BobId && t.ProjectName != "");
        page.Items.Should().Contain(t => t.Title == "for bob" && t.ProjectName == "S19 project");
    }

    [Fact]
    public async Task S20_Expired_Access_Token_Then_Refresh_Gives_Working_Tokens()
    {
        var login = await factory.LoginAsync(F.BobEmail);
        var expired = await ForgeExpiredTokenAsync(F.BobEmail);
        (await factory.CreateClient().WithBearer(expired).GetAsync("/api/projects")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.RefreshToken });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = await res.DataAsync<AuthResponse>();
        (await factory.CreateClient().WithBearer(fresh.AccessToken).GetAsync("/api/projects")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<(ProjectFixture P, string Url)> ProjectWithTaskAsync(string child)
    {
        var p = await factory.CreateProjectWithCastAsync();
        var task = await p.CreateTaskAsync(p.Alice, new { title = "T" });
        return (p, $"{p.Tasks}/{task.Id}/{child}");
    }

    /// <summary>A correctly signed token for a real user (current stamp) that expired 5 minutes ago.</summary>
    private async Task<string> ForgeExpiredTokenAsync(string email)
    {
        var opt = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        using var scope = factory.Services.CreateScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
        var expires = DateTime.UtcNow.AddMinutes(-5);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = opt.Issuer,
            Audience = opt.Audience,
            Subject = new ClaimsIdentity([
                new Claim(AuthClaimTypes.Subject, user.Id.ToString()),
                new Claim(AuthClaimTypes.SecurityStamp, TokenHasher.Hash(user.SecurityStamp)),
            ]),
            IssuedAt = expires.AddMinutes(-15),
            NotBefore = expires.AddMinutes(-15),
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Key)), SecurityAlgorithms.HmacSha256),
        });
    }
}
