using FluentValidation;
using TaskTracker.Application.Common;
using TaskTracker.Domain.Enums;

namespace TaskTracker.Application.Projects;

public record ProjectDto(
    Guid Id, string Name, string? Description,
    ProjectRole MyRole, Guid CreatedByUserId,
    DateTime CreatedAt, DateTime UpdatedAt);

public record MemberDto(Guid UserId, string Email, string DisplayName, ProjectRole Role, DateTime JoinedAt);

public record CreateProjectRequest(string Name, string? Description);

/// <summary>PATCH: only fields present in the JSON are applied. "description": null clears it.</summary>
public record UpdateProjectRequest(Optional<string> Name, Optional<string?> Description);

public record AddMemberRequest(string Email, ProjectRole Role);

public record ChangeMemberRoleRequest(ProjectRole Role);

public record TransferOwnershipRequest(Guid NewOwnerUserId);

/// <summary>GET /api/projects — always limited to projects the caller belongs to (Admins: all).</summary>
public class ProjectListQuery : SortedPageQuery
{
    public static readonly string[] SortFields = ["name", "createdAt", "updatedAt"];

    public string? Search { get; set; }
    /// <summary>Only projects where the caller has exactly this role.</summary>
    public ProjectRole? Role { get; set; }
}

public class ProjectListQueryValidator : AbstractValidator<ProjectListQuery>
{
    public ProjectListQueryValidator()
    {
        this.AddSortedPageRules(ProjectListQuery.SortFields);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name.Value).NotEmpty().MaximumLength(100).OverridePropertyName("Name").When(x => x.Name.IsSet);
        RuleFor(x => x.Description.Value).MaximumLength(1000).OverridePropertyName("Description").When(x => x.Description.IsSet);
    }
}

public class AddMemberRequestValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Role).IsInEnum();
    }
}

public class ChangeMemberRoleRequestValidator : AbstractValidator<ChangeMemberRoleRequest>
{
    public ChangeMemberRoleRequestValidator() => RuleFor(x => x.Role).IsInEnum();
}

public class TransferOwnershipRequestValidator : AbstractValidator<TransferOwnershipRequest>
{
    public TransferOwnershipRequestValidator() => RuleFor(x => x.NewOwnerUserId).NotEmpty();
}

public interface IProjectService
{
    /// <summary>The caller's effective role in the project, or null when they are not a member (→ 404).</summary>
    Task<ProjectAccess?> GetAccessAsync(Guid projectId, CurrentUser user);

    Task<PagedResult<ProjectDto>> ListAsync(CurrentUser user, ProjectListQuery query);
    Task<ProjectDto> CreateAsync(CurrentUser user, CreateProjectRequest request);
    Task<ProjectDto> GetAsync(ProjectAccess access);
    Task<ProjectDto> UpdateAsync(ProjectAccess access, CurrentUser user, UpdateProjectRequest request);
    Task DeleteAsync(ProjectAccess access, CurrentUser user);

    Task<IReadOnlyList<MemberDto>> ListMembersAsync(ProjectAccess access);
    Task<ServiceResult<MemberDto>> AddMemberAsync(ProjectAccess access, CurrentUser user, AddMemberRequest request);
    Task<ServiceResult<MemberDto>> ChangeMemberRoleAsync(ProjectAccess access, CurrentUser user, Guid memberUserId, ProjectRole role);
    Task<ServiceResult<bool>> RemoveMemberAsync(ProjectAccess access, CurrentUser user, Guid memberUserId);
    Task<ServiceResult<IReadOnlyList<MemberDto>>> TransferOwnershipAsync(ProjectAccess access, CurrentUser user, Guid newOwnerUserId);
}
