using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskTracker.Api.Tests.Infrastructure;
using TaskTracker.Application.Auth;
using TaskTracker.Domain;
using TaskTracker.Domain.Enums;
using TaskTracker.Infrastructure.Persistence;
using F = TaskTracker.Api.Tests.Infrastructure.TaskTrackerWebAppFactory;

namespace TaskTracker.Api.Tests.Seeding;

public class SeedDataTests(F factory) : IClassFixture<F>
{
    [Theory]
    [InlineData(F.AdminEmail, Roles.Admin)]
    [InlineData(F.AliceEmail, Roles.User)]
    [InlineData(F.BobEmail, Roles.User)]
    [InlineData(F.CarolEmail, Roles.User)]
    [InlineData(F.DaveEmail, Roles.User)]
    public async Task Seeded_Users_Can_Log_In_With_Their_Global_Role(string email, string role)
    {
        var res = await factory.CreateClient().LoginAsync(email);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.ReadEnvelopeAsync<AuthResponse>()).Data!.User.Roles.Should().Equal(role);
    }

    [Fact]
    public async Task Seed_Creates_Website_Redesign_With_The_TASKS_md_Cast_And_Content()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects
            .Where(p => p.Name == DbInitializer.DemoProjectName)
            .Select(p => new
            {
                Members = p.Members.Select(m => new { m.User.Email, m.Role }).ToList(),
                Tasks = p.Tasks.Select(t => new { t.Status, t.CompletedAt, Comments = t.Comments.Count, Logs = t.TimeLogs.Count }).ToList(),
            })
            .SingleAsync();

        project.Members.Should().BeEquivalentTo(new[]
        {
            new { Email = (string?)F.AliceEmail, Role = ProjectRole.Owner },
            new { Email = (string?)F.BobEmail, Role = ProjectRole.Member },
            new { Email = (string?)F.CarolEmail, Role = ProjectRole.Viewer },
        });
        project.Tasks.Should().HaveCount(5);
        project.Tasks.Select(t => t.Status).Distinct().Should().HaveCount(5, "every status is represented");
        project.Tasks.Should().OnlyContain(t => (t.Status == Domain.Enums.TaskStatus.Done) == (t.CompletedAt != null));
        project.Tasks.Sum(t => t.Comments).Should().Be(2);
        project.Tasks.Sum(t => t.Logs).Should().Be(2);
    }

    [Fact]
    public async Task Seeding_Is_Idempotent()
    {
        await DbInitializer.InitializeAsync(factory.Services, isDevelopment: true);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Projects.CountAsync(p => p.Name == DbInitializer.DemoProjectName)).Should().Be(1);
        (await db.Users.CountAsync(u => u.Email!.EndsWith("@example.com"))).Should().Be(5);
    }
}
