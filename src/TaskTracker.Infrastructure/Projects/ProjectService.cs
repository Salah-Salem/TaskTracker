using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskTracker.Application.Common;
using TaskTracker.Application.Projects;
using TaskTracker.Domain.Entities;
using TaskTracker.Domain.Enums;
using TaskTracker.Infrastructure.Common;
using TaskTracker.Infrastructure.Persistence;

namespace TaskTracker.Infrastructure.Projects;

public class ProjectService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    TimeProvider clock,
    ILogger<ProjectService> log) : IProjectService
{
    public const string UserNotFound = "User not found";
    public const string MemberNotFound = "Member not found";
    public const string AlreadyMember = "User is already a member of this project";
    public const string UseTransfer = "Owner cannot be granted directly; use transfer-ownership";
    public const string LastOwner = "A project must always have an Owner; transfer ownership first";
    public const string NewOwnerNotMember = "New owner must already be a member of the project";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ProjectAccess?> GetAccessAsync(Guid projectId, CurrentUser user)
    {
        var role = await db.ProjectMembers
            .Where(m => m.ProjectId == projectId && m.UserId == user.Id)
            .Select(m => (ProjectRole?)m.Role)
            .SingleOrDefaultAsync();

        if (user.IsAdmin)
            // System Admins act as Owner on every (non-deleted) project.
            return role is not null || await db.Projects.AnyAsync(p => p.Id == projectId)
                ? new ProjectAccess(projectId, ProjectRole.Owner, IsAdminOverride: role != ProjectRole.Owner)
                : null;

        return role is null ? null : new ProjectAccess(projectId, role.Value, IsAdminOverride: false);
    }

    public async Task<PagedResult<ProjectDto>> ListAsync(CurrentUser user, ProjectListQuery query)
    {
        var q = db.Projects.AsNoTracking();

        if (!user.IsAdmin)
            q = q.Where(p => p.Members.Any(m => m.UserId == user.Id));
        if (query.Role is { } role)
            q = q.Where(p => p.Members.Any(m => m.UserId == user.Id && m.Role == role));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = QueryRules.LikeContains(query.Search);
            q = q.Where(p => EF.Functions.Like(p.Name, like, "\\")
                          || (p.Description != null && EF.Functions.Like(p.Description, like, "\\")));
        }

        var desc = query.Descending;
        var sorted = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => q.OrderByDir(p => p.Name.ToLower(), desc),
            "updatedat" => q.OrderByDir(p => p.UpdatedAt, desc),
            _ => q.OrderByDir(p => p.CreatedAt, desc),
        };

        var isAdmin = user.IsAdmin;
        return await sorted.ThenBy(p => p.Id)
            .Select(p => new ProjectDto(
                p.Id, p.Name, p.Description,
                isAdmin ? ProjectRole.Owner : p.Members.Where(m => m.UserId == user.Id).Select(m => m.Role).First(),
                p.CreatedByUserId, p.CreatedAt, p.UpdatedAt))
            .ToPagedAsync(query);
    }

    public async Task<ProjectDto> CreateAsync(CurrentUser user, CreateProjectRequest req)
    {
        var now = Now;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            CreatedByUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
            // Creator becomes the Owner — any authenticated user may create a project.
            Members = { new ProjectMember { UserId = user.Id, Role = ProjectRole.Owner, JoinedAt = now } },
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        log.LogInformation("Audit: user {UserId} created project {ProjectId}", user.Id, project.Id);
        return ToDto(project, ProjectRole.Owner);
    }

    public async Task<ProjectDto> GetAsync(ProjectAccess access)
    {
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == access.ProjectId);
        return ToDto(project, access.Role);
    }

    public async Task<ProjectDto> UpdateAsync(ProjectAccess access, CurrentUser user, UpdateProjectRequest req)
    {
        var project = await db.Projects.SingleAsync(p => p.Id == access.ProjectId);
        if (req.Name.IsSet) project.Name = req.Name.Value.Trim();
        if (req.Description.IsSet) project.Description = req.Description.Value?.Trim();
        project.UpdatedAt = Now;
        await db.SaveChangesAsync();

        log.LogInformation("Audit: user {UserId} updated project {ProjectId}", user.Id, project.Id);
        return ToDto(project, access.Role);
    }

    public async Task DeleteAsync(ProjectAccess access, CurrentUser user)
    {
        var project = await db.Projects.SingleAsync(p => p.Id == access.ProjectId);
        project.IsDeleted = true; // soft delete: the global filter hides it and everything under it
        project.UpdatedAt = Now;
        await db.SaveChangesAsync();

        log.LogInformation("Audit: user {UserId} deleted project {ProjectId}", user.Id, project.Id);
    }

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(ProjectAccess access) =>
        await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == access.ProjectId)
            .OrderByDescending(m => m.Role).ThenBy(m => m.User.DisplayName)
            .Select(m => new MemberDto(m.UserId, m.User.Email!, m.User.DisplayName, m.Role, m.JoinedAt))
            .ToListAsync();

    public async Task<ServiceResult<MemberDto>> AddMemberAsync(ProjectAccess access, CurrentUser user, AddMemberRequest req)
    {
        if (req.Role == ProjectRole.Owner)
            return ServiceResult<MemberDto>.Conflict(UseTransfer);

        var target = await users.FindByEmailAsync(req.Email.Trim());
        if (target is null)
            return ServiceResult<MemberDto>.NotFound(UserNotFound);

        if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == access.ProjectId && m.UserId == target.Id))
            return ServiceResult<MemberDto>.Conflict(AlreadyMember);

        var member = new ProjectMember { ProjectId = access.ProjectId, UserId = target.Id, Role = req.Role, JoinedAt = Now };
        db.ProjectMembers.Add(member);
        await db.SaveChangesAsync();

        log.LogInformation("Audit: user {UserId} added {MemberId} to project {ProjectId} as {Role}",
            user.Id, target.Id, access.ProjectId, req.Role);
        return ServiceResult<MemberDto>.Ok(new MemberDto(target.Id, target.Email!, target.DisplayName, member.Role, member.JoinedAt));
    }

    public async Task<ServiceResult<MemberDto>> ChangeMemberRoleAsync(ProjectAccess access, CurrentUser user, Guid memberUserId, ProjectRole role)
    {
        if (role == ProjectRole.Owner)
            return ServiceResult<MemberDto>.Conflict(UseTransfer);

        var member = await db.ProjectMembers.Include(m => m.User)
            .SingleOrDefaultAsync(m => m.ProjectId == access.ProjectId && m.UserId == memberUserId);
        if (member is null)
            return ServiceResult<MemberDto>.NotFound(MemberNotFound);
        if (member.Role == ProjectRole.Owner)
            return ServiceResult<MemberDto>.Conflict(LastOwner);

        await using var tx = await db.Database.BeginTransactionAsync();
        var old = member.Role;
        member.Role = role;
        await db.SaveChangesAsync();
        if (role == ProjectRole.Viewer)
            await UnassignAsync(access.ProjectId, memberUserId); // Viewers cannot hold assignments
        await tx.CommitAsync();

        log.LogInformation("Audit: user {UserId} changed {MemberId} in project {ProjectId} from {Old} to {New}",
            user.Id, memberUserId, access.ProjectId, old, role);
        return ServiceResult<MemberDto>.Ok(new MemberDto(member.UserId, member.User.Email!, member.User.DisplayName, member.Role, member.JoinedAt));
    }

    public async Task<ServiceResult<bool>> RemoveMemberAsync(ProjectAccess access, CurrentUser user, Guid memberUserId)
    {
        var member = await db.ProjectMembers
            .SingleOrDefaultAsync(m => m.ProjectId == access.ProjectId && m.UserId == memberUserId);
        if (member is null)
            return ServiceResult<bool>.NotFound(MemberNotFound);
        if (member.Role == ProjectRole.Owner)
            return ServiceResult<bool>.Conflict(LastOwner);

        await using var tx = await db.Database.BeginTransactionAsync();
        db.ProjectMembers.Remove(member);
        await db.SaveChangesAsync();
        await UnassignAsync(access.ProjectId, memberUserId);
        await tx.CommitAsync();

        log.LogInformation("Audit: user {UserId} removed {MemberId} from project {ProjectId}", user.Id, memberUserId, access.ProjectId);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<IReadOnlyList<MemberDto>>> TransferOwnershipAsync(ProjectAccess access, CurrentUser user, Guid newOwnerUserId)
    {
        var members = await db.ProjectMembers.Where(m => m.ProjectId == access.ProjectId).ToListAsync();
        var newOwner = members.SingleOrDefault(m => m.UserId == newOwnerUserId);
        if (newOwner is null)
            return ServiceResult<IReadOnlyList<MemberDto>>.BadRequest(NewOwnerNotMember);

        if (newOwner.Role != ProjectRole.Owner)
        {
            foreach (var owner in members.Where(m => m.Role == ProjectRole.Owner))
                owner.Role = ProjectRole.Member; // old Owner stays on the project as a Member
            newOwner.Role = ProjectRole.Owner;
            await db.SaveChangesAsync();
            log.LogInformation("Audit: user {UserId} transferred ownership of project {ProjectId} to {NewOwnerId}",
                user.Id, access.ProjectId, newOwnerUserId);
        }

        return ServiceResult<IReadOnlyList<MemberDto>>.Ok(await ListMembersAsync(access));
    }

    private Task<int> UnassignAsync(Guid projectId, Guid userId)
    {
        var now = Now;
        return db.Tasks
            .Where(t => t.ProjectId == projectId && t.AssigneeId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (Guid?)null).SetProperty(t => t.UpdatedAt, now));
    }

    private static ProjectDto ToDto(Project p, ProjectRole myRole) =>
        new(p.Id, p.Name, p.Description, myRole, p.CreatedByUserId, p.CreatedAt, p.UpdatedAt);
}
