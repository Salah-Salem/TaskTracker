using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskTracker.Domain;
using TaskTracker.Domain.Entities;
using TaskTracker.Domain.Enums;
using TaskStatus = TaskTracker.Domain.Enums.TaskStatus;

namespace TaskTracker.Infrastructure.Persistence;

public static class DbInitializer
{
    public const string DemoPassword = "Passw0rd!";
    public const string DemoProjectName = "Website Redesign";

    /// <summary>
    /// Applies migrations, ensures the global roles exist (every environment), and in Development
    /// seeds the demo data. Idempotent.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, bool isDevelopment)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        if (isDevelopment)
            await db.Database.MigrateAsync();

        // Registration assigns "User", so roles must exist before the first request.
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var name in Roles.All)
            if (!await roles.RoleExistsAsync(name))
                await roles.CreateAsync(new IdentityRole<Guid>(name) { Id = Guid.NewGuid() });

        if (isDevelopment)
            await SeedDemoDataAsync(sp, db);
    }

    /// <summary>
    /// admin@example.com (system Admin) plus the TASKS.md cast:
    /// alice = Owner, bob = Member, carol = Viewer of "Website Redesign"; dave = not a member.
    /// </summary>
    private static async Task SeedDemoDataAsync(IServiceProvider sp, AppDbContext db)
    {
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));

        // "Already seeded" marker: the demo admin exists.
        if (await users.FindByEmailAsync("admin@example.com") is not null)
            return;

        await CreateUserAsync(users, "admin@example.com", "Ada Admin", Roles.Admin);
        var alice = await CreateUserAsync(users, "alice@example.com", "Alice", Roles.User);
        var bob   = await CreateUserAsync(users, "bob@example.com", "Bob", Roles.User);
        var carol = await CreateUserAsync(users, "carol@example.com", "Carol", Roles.User);
        _         = await CreateUserAsync(users, "dave@example.com", "Dave", Roles.User);

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = DemoProjectName, Description = "Refresh the public website and blog.",
            CreatedByUserId = alice.Id, CreatedAt = now, UpdatedAt = now,
            Members =
            {
                new ProjectMember { UserId = alice.Id, Role = ProjectRole.Owner, JoinedAt = now },
                new ProjectMember { UserId = bob.Id, Role = ProjectRole.Member, JoinedAt = now },
                new ProjectMember { UserId = carol.Id, Role = ProjectRole.Viewer, JoinedAt = now },
            },
        };
        db.Projects.Add(project);

        TaskItem NewTask(string title, TaskStatus status, TaskPriority priority, ApplicationUser? assignee, DateOnly? due, int ageMinutes)
        {
            var created = now.AddMinutes(-ageMinutes);
            var t = new TaskItem
            {
                Id = Guid.NewGuid(), ProjectId = project.Id, Title = title, Priority = priority,
                AssigneeId = assignee?.Id, DueDate = due, CreatedById = alice.Id, CreatedAt = created, UpdatedAt = created,
            };
            t.SetStatus(status, created);
            return t;
        }

        var loginBug = NewTask("Fix login redirect bug", TaskStatus.Todo, TaskPriority.High, bob, today.AddDays(3), 50);
        var landing  = NewTask("Design new landing page", TaskStatus.InProgress, TaskPriority.Medium, alice, today.AddDays(7), 40);
        db.Tasks.AddRange(
            loginBug,
            landing,
            NewTask("Write copy for login page", TaskStatus.InReview, TaskPriority.Low, bob, today.AddDays(5), 30),
            NewTask("Set up analytics", TaskStatus.Done, TaskPriority.Medium, null, today.AddDays(-2), 20),
            NewTask("Migrate old blog posts", TaskStatus.Cancelled, TaskPriority.Low, null, null, 10));

        db.Comments.AddRange(
            new Comment { Id = Guid.NewGuid(), TaskId = loginBug.Id, AuthorId = alice.Id, Body = "Repro: log in from /pricing and you land on /home.", CreatedAt = now.AddMinutes(-45), UpdatedAt = now.AddMinutes(-45) },
            new Comment { Id = Guid.NewGuid(), TaskId = loginBug.Id, AuthorId = bob.Id, Body = "On it — looks like the returnUrl is dropped.", CreatedAt = now.AddMinutes(-35), UpdatedAt = now.AddMinutes(-35) });

        db.TimeLogs.AddRange(
            new TimeLog { Id = Guid.NewGuid(), TaskId = loginBug.Id, UserId = bob.Id, Minutes = 90, WorkDate = today.AddDays(-1), Note = "Investigated redirect flow", CreatedAt = now.AddMinutes(-30) },
            new TimeLog { Id = Guid.NewGuid(), TaskId = landing.Id, UserId = alice.Id, Minutes = 45, WorkDate = today, Note = "Wireframes", CreatedAt = now.AddMinutes(-25) });

        await db.SaveChangesAsync();
        log.LogInformation("Seeded demo data: 5 users, project '{Project}' with 5 tasks, 2 comments, 2 time logs", DemoProjectName);
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> users, string email, string displayName, string role)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, DisplayName = displayName };
        var created = await users.CreateAsync(user, DemoPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException($"Seeding {email} failed: " +
                string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return user;
    }
}
