using TaskTracker.Domain.Enums;

namespace TaskTracker.Application.Common;

/// <summary>The caller, as far as the application layer cares.</summary>
public record CurrentUser(Guid Id, bool IsAdmin);

/// <summary>
/// The caller's effective role in one project. Only produced for members (or system Admins,
/// who act as Owner everywhere) — a null access means "not a member" and maps to 404.
/// This is the resource the <c>ProjectMember / ProjectContributor / ProjectOwner</c> policies evaluate.
/// </summary>
public record ProjectAccess(Guid ProjectId, ProjectRole Role, bool IsAdminOverride)
{
    public bool IsAtLeast(ProjectRole role) => Role >= role;
}

/// <summary>A comment or time log, for the "author or project Owner" policies.</summary>
public record AuthoredResource(Guid AuthorId, ProjectAccess Access);
