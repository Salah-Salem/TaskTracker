using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskTracker.Application.Activity;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Projects;
using TaskTracker.Application.Tasks;
using TaskTracker.Application.Users;
using TaskTracker.Domain.Entities;
using TaskTracker.Infrastructure.Activity;
using TaskTracker.Infrastructure.Auth;
using TaskTracker.Infrastructure.Persistence;
using TaskTracker.Infrastructure.Projects;
using TaskTracker.Infrastructure.Tasks;
using TaskTracker.Infrastructure.Users;

namespace TaskTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite(cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is missing.")));

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                // Policy per US-A1: minimum length 8. Complexity rules are deliberately off to match the brief.
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 1;

                o.User.RequireUniqueEmail = true;

                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.Configure<JwtOptions>(cfg.GetSection(JwtOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<JwtTokenService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<ITimeLogService, TimeLogService>();

        return services;
    }
}
