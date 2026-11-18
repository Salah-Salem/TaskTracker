using Microsoft.AspNetCore.Authorization;
using TaskTracker.Application.Common;
using TaskTracker.Domain;
using TaskTracker.Domain.Enums;

namespace TaskTracker.Api.Auth;

/// <summary>
/// Policy names. The project-scoped ones are resource-based: controllers call
/// <see cref="IAuthorizationService"/> with a loaded <see cref="ProjectAccess"/> / <see cref="AuthoredResource"/>.
/// </summary>
public static class Policies
{
    public const string AdminOnly = "AdminOnly";

    public const string ProjectMember = "ProjectMember";           // rank ≥ Viewer (reads)
    public const string ProjectContributor = "ProjectContributor"; // rank ≥ Member (create/edit)
    public const string ProjectOwner = "ProjectOwner";             // rank ≥ Owner  (delete, membership)

    public const string CommentAuthorOrProjectOwner = "CommentAuthorOrProjectOwner";
    public const string TimeLogAuthorOrProjectOwner = "TimeLogAuthorOrProjectOwner";

    public static void Register(AuthorizationOptions o)
    {
        o.AddPolicy(AdminOnly, p => p.RequireRole(Roles.Admin));

        o.AddPolicy(ProjectMember, p => p.AddRequirements(new ProjectRoleRequirement(ProjectRole.Viewer)));
        o.AddPolicy(ProjectContributor, p => p.AddRequirements(new ProjectRoleRequirement(ProjectRole.Member)));
        o.AddPolicy(ProjectOwner, p => p.AddRequirements(new ProjectRoleRequirement(ProjectRole.Owner)));

        o.AddPolicy(CommentAuthorOrProjectOwner, p => p.AddRequirements(new AuthorOrProjectOwnerRequirement()));
        o.AddPolicy(TimeLogAuthorOrProjectOwner, p => p.AddRequirements(new AuthorOrProjectOwnerRequirement()));
    }
}

public sealed record ProjectRoleRequirement(ProjectRole MinimumRole) : IAuthorizationRequirement;

/// <summary>Succeeds when the caller's role in the project is at least the required rank.</summary>
public sealed class ProjectRoleHandler : AuthorizationHandler<ProjectRoleRequirement, ProjectAccess>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ProjectRoleRequirement requirement, ProjectAccess access)
    {
        if (access.IsAtLeast(requirement.MinimumRole))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed record AuthorOrProjectOwnerRequirement : IAuthorizationRequirement;

/// <summary>Comment / time-log edit+delete: the author, or the project Owner (moderation).</summary>
public sealed class AuthorOrProjectOwnerHandler : AuthorizationHandler<AuthorOrProjectOwnerRequirement, AuthoredResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AuthorOrProjectOwnerRequirement requirement, AuthoredResource resource)
    {
        var userId = context.User.FindFirst("sub")?.Value;
        if (resource.Access.IsAtLeast(ProjectRole.Owner) || resource.AuthorId.ToString() == userId)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
